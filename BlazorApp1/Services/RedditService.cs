using System.Drawing;
using HtmlAgilityPack;
using SkiaSharp;

namespace BlazorApp1.Services;

public record PostData(string Title, string Body);

public class RedditException(string message, Exception? inner = null)
    : Exception(message, inner);

public class RedditPostNotFoundException(string url, string reason)
    : RedditException($"Could not find post content at {url}: {reason}");

public class RedditFetchException(string url, string reason, Exception inner)
    : RedditException($"Failed to fetch {url}: {reason}", inner);

public class RedditService(IHttpClientFactory clientFactory)
{
    private static readonly string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public async Task<PostData> GetPost(string url)
    {
        var client = clientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromSeconds(10);

        var resolved = await ResolveUrl(url, client);

        string html;
        try
        {
            html = await client.GetStringAsync(resolved);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RedditFetchException(url, ex.Message, ex);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class, 'title') and contains(@class, 'may-blank')]")?.InnerText.Trim();

        if (title is null)
            throw new RedditPostNotFoundException(url, "title not found");

        var body = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'thing') and contains(@data-type, 'link')]")
            ?.SelectSingleNode(".//div[contains(@class, 'usertext-body')]//div[contains(@class, 'md')]")
            ?.InnerText.Trim();

        if (body is null)
            throw new RedditPostNotFoundException(url, "body not found");

        return new PostData(title, body);
    }

    private static async Task<string> ResolveUrl(string url, HttpClient client)
    {
        Uri uri;
        try
        {
            uri = new Uri(url);
        }
        catch (UriFormatException ex)
        {
            throw new RedditFetchException(url, $"invalid URL: {ex.Message}", ex);
        }

        if (uri.Host == "old.reddit.com" && uri.AbsolutePath.Contains("/comments/"))
            return url;

        if (uri.AbsolutePath.Contains("/s/"))
        {
            var resolveUri = new UriBuilder(uri) { Host = "www.reddit.com" }.Uri;

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, resolveUri),
                    HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new RedditFetchException(url, $"resolve failed: {ex.Message}", ex);
            }

            using (response)
            {
                var finalUri = response.RequestMessage?.RequestUri ?? resolveUri;
                var path = finalUri.AbsolutePath;
                if (path.Contains("/comments/"))
                    return $"https://old.reddit.com{path}";
            }
        }

        return new UriBuilder(uri) { Host = "old.reddit.com" }.Uri.ToString();
    }

    public async Task GenerateTitleCard(string title)
    {
        int w = 1080, h = 1920;
        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Card dimensions — centered on a 1080x1920 canvas
        float cardW = 920, cardH = 600;
        float cardX = (w - cardW) / 2;
        float cardY = (h - cardH) / 2;
        float cornerRadius = 20;
        float padding = 40;

        var bgPaint = new SKPaint
        {
            Color = new SKColor(26, 26, 27, 240),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, cornerRadius, cornerRadius, bgPaint);

        var typeface = SKTypeface.Default;
        var font = new SKFont(typeface, 38);
        var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        var wrappedLines = WrapText(title, (int)(cardW - padding * 2), font);
        if (wrappedLines.Count == 0) wrappedLines.Add("");

        // Total text block height
        float lineHeight = 50;
        float textBlockHeight = wrappedLines.Count * lineHeight;

        // Vertically center the text block within the card
        float startY = cardY + (cardH - textBlockHeight) / 2 + lineHeight * 0.8f;

        foreach (var line in wrappedLines)
        {
            // Horizontally center each line within the card
            canvas.DrawText(line, cardX + cardW / 2, startY, SKTextAlign.Center, font, textPaint);
            startY += lineHeight;
        }
        
        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync("/Volumes/realme/Dev/content-farm/reddit/ballers.png", data.ToArray());
    }

    private static List<string> WrapText(string text, int maxWidth, SKFont font)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = new List<string>();

        foreach (var word in words)
        {
            var testLine = currentLine.Count == 0
                ? word
                : string.Join(" ", currentLine.Append(word));

            float width = font.MeasureText(testLine);

            if (width <= maxWidth)
            {
                currentLine.Add(word);
            }
            else
            {
                if (currentLine.Count > 0)
                {
                    lines.Add(string.Join(" ", currentLine));
                    currentLine = new List<string> { word };
                }
                else
                {
                    // Single word wider than maxWidth — add it anyway
                    lines.Add(word);
                }
            }
        }

        if (currentLine.Count > 0)
            lines.Add(string.Join(" ", currentLine));

        return lines;
    }
}
