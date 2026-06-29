//
// Edge TTS WebSocket API — quirks:
//
// • Auth uses Sec-MS-GEC, a SHA256 of (WindowsFileTime rounded to 5min + client token).
//   The Sec-MS-GEC-Version must match the current Chromium version (1-143.0.3650.75).
//   If the token is wrong or the clock is skewed, the server returns 403.
// • All requests AND the WebSocket upgrade need specific headers (User-Agent, Origin, muid cookie,
//   Accept-Encoding, etc.) — missing any of them causes 400/403.
// • Every text frame needs X-Timestamp in JS date format or the server rejects it.
// • Binary frames encode header length in the first 2 bytes (big-endian), followed by text headers
//   ("Path:audio\r\nContent-Type:audio/mpeg\r\n\r\n"), then raw MP3 data.
// • WordBoundary metadata comes in text frames with Path:audio.metadata. Offset and Duration
//   are in 100ns ticks. Set wordBoundaryEnabled":"true" in the speech config to receive them.
// • The connection is one-shot — the server sends Path:turn.end when done. No keepalive needed.
// • The audio format is CBR MP3 at 48kbps, 24kHz mono. Byte count can be used to compensate
//   for inter-chunk drift in long texts if needed.
//

using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RedditShortMaker.Services;

public record WordBoundary(string Text, long OffsetTicks, long DurationTicks)
{
    public double StartSec => OffsetTicks / 10_000_000.0;
    public double EndSec => (OffsetTicks + DurationTicks) / 10_000_000.0;
}

public record EdgeTtsResult(byte[] Audio, IReadOnlyList<WordBoundary> WordBoundaries);

public class EdgeTtsException(string message, Exception? inner = null)
    : Exception(message, inner);

public class EdgeTtsService
{
    private const string ChromiumVersion = "143.0.3650.75";
    private const string ChromiumMajor = "143";

    private static readonly string UserAgent =
        $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        $"(KHTML, like Gecko) Chrome/{ChromiumMajor}.0.0.0 Safari/537.36 Edg/{ChromiumMajor}.0.0.0";

    public async Task<EdgeTtsResult> SynthesizeAsync(
        string text,
        string voice = "en-US-GuyNeural",
        string lang = "en-US",
        int rate = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty", nameof(text));

        var audio = new MemoryStream();
        var boundaries = new List<WordBoundary>();
        var requestId = Guid.NewGuid().ToString("N");
        var connectionId = Guid.NewGuid().ToString("N");

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent", UserAgent);
        ws.Options.SetRequestHeader("Origin",
            "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("Cookie", $"muid={GenerateMuid()};");

        var timestamp = DateTime.UtcNow.ToString(
            "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'");

        var uri = $"wss://speech.platform.bing.com/consumer/speech/synthesize/" +
                  $"readaloud/edge/v1?TrustedClientToken=6A5AA1D4EAFF4E9FB37E23D68491D6F4" +
                  $"&ConnectionId={connectionId}" +
                  $"&Sec-MS-GEC={GenerateToken()}" +
                  $"&Sec-MS-GEC-Version=1-{ChromiumVersion}";

        try
        {
            await ws.ConnectAsync(new Uri(uri), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EdgeTtsException($"Failed to connect to Edge TTS: {ex.Message}", ex);
        }

        try
        {
            var config =
                $"X-Timestamp:{timestamp}Z\r\n" +
                "Content-Type:application/json; charset=utf-8\r\n" +
                "Path:speech.config\r\n\r\n" +
                """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"true"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}""";
            await SendTextAsync(ws, Encoding.UTF8.GetBytes(config), ct);

            var ssml =
                $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{lang}'>" +
                $"<voice name='{voice}'>" +
                $"<prosody pitch='+0Hz' rate='{rate}%' volume='100'>{EscapeXml(text)}</prosody>" +
                "</voice></speak>";

            var ssmlRequest =
                $"X-RequestId:{requestId}\r\n" +
                "Content-Type:application/ssml+xml\r\n" +
                $"X-Timestamp:{timestamp}Z\r\n" +
                "Path:ssml\r\n\r\n" +
                ssml;
            await SendTextAsync(ws, Encoding.UTF8.GetBytes(ssmlRequest), ct);

            var buffer = new byte[65536];
            while (ws.State == WebSocketState.Open)
            {
                var frame = await ReceiveFrameAsync(ws, buffer, ct);

                if (frame.Type == WebSocketMessageType.Close)
                    break;

                if (frame.Type == WebSocketMessageType.Text)
                {
                    var payload = Encoding.UTF8.GetString(frame.Data.Span);

                    if (payload.Contains("Path:turn.end"))
                        break;

                    if (payload.Contains("Path:audio.metadata"))
                        ParseMetadata(payload, boundaries);
                }
                else if (frame.Type == WebSocketMessageType.Binary)
                {
                    var data = frame.Data.Span;
                    if (data.Length < 2) continue;

                    var headerLength = (data[0] << 8) | data[1];
                    if (headerLength + 2 > data.Length) continue;

                    audio.Write(data[(headerLength + 2)..]);
                }
            }
        }
        catch (WebSocketException ex)
        {
            throw new EdgeTtsException($"WebSocket error during synthesis: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); }
                catch { }
        }

        if (audio.Length == 0)
            throw new EdgeTtsException("No audio data received from Edge TTS");

        return new EdgeTtsResult(audio.ToArray(), boundaries.AsReadOnly());
    }

    private static async Task SendTextAsync(ClientWebSocket ws, ReadOnlyMemory<byte> utf8Bytes, CancellationToken ct)
    {
        await ws.SendAsync(utf8Bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<(WebSocketMessageType Type, ReadOnlyMemory<byte> Data)> ReceiveFrameAsync(
        ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return (result.MessageType, ms.ToArray());
    }

    private static void ParseMetadata(string payload, List<WordBoundary> boundaries)
    {
        var jsonStart = payload.IndexOf('{');
        if (jsonStart < 0) return;

        try
        {
            using var doc = JsonDocument.Parse(payload[jsonStart..]);

            if (!doc.RootElement.TryGetProperty("Metadata", out var metadata))
                return;

            foreach (var item in metadata.EnumerateArray())
            {
                if (item.GetProperty("Type").GetString() != "WordBoundary")
                    continue;

                var data = item.GetProperty("Data");
                var text = data.GetProperty("text").GetProperty("Text").GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                boundaries.Add(new WordBoundary(
                    text,
                    data.GetProperty("Offset").GetInt64(),
                    data.GetProperty("Duration").GetInt64()
                ));
            }
        }
        catch (JsonException) { }
    }

    private static string GenerateToken()
    {
        var ticks = DateTime.UtcNow.ToFileTimeUtc();
        ticks -= ticks % 3_000_000_000;
        var hash = SHA256.HashData(
            Encoding.ASCII.GetBytes($"{ticks}6A5AA1D4EAFF4E9FB37E23D68491D6F4")
        );
        return Convert.ToHexString(hash);
    }

    private static string GenerateMuid()
    {
        var bytes = new byte[16];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes);
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&apos;");
}
