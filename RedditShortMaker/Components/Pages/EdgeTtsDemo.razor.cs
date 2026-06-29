using Microsoft.AspNetCore.Components;
using RedditShortMaker.Services;

namespace RedditShortMaker.Components.Pages;

public partial class EdgeTtsDemo
{
    [Inject]
    private EdgeTtsService EdgeTts { get; set; } = null!;

    private string text = "Hello, this is a demonstration of the Edge text-to-speech service.";
    private string voice = "en-US-GuyNeural";
    private int rate;
    private bool busy;
    private string? error;
    private string? audioDataUrl;
    private long? audioSizeBytes;
    private List<WordBoundary> wordBoundaries = [];

    private static readonly List<(string Value, string Label)> Voices =
    [
        ("en-US-GuyNeural", "🇺🇸 Guy (US)"),
        ("en-US-AriaNeural", "🇺🇸 Aria (US)"),
        ("en-US-JennyNeural", "🇺🇸 Jenny (US)"),
        ("en-US-ChristopherNeural", "🇺🇸 Christopher (US)"),
        ("en-US-EricNeural", "🇺🇸 Eric (US)"),
        ("en-GB-RyanNeural", "🇬🇧 Ryan (UK)"),
        ("en-GB-SoniaNeural", "🇬🇧 Sonia (UK)"),
        ("en-GB-LibbyNeural", "🇬🇧 Libby (UK)"),
        ("en-AU-WilliamNeural", "🇦🇺 William (AU)"),
        ("en-AU-NatashaNeural", "🇦🇺 Natasha (AU)"),
        ("en-IN-NeerjaNeural", "🇮🇳 Neerja (IN)"),
        ("ja-JP-KeitaNeural", "🇯🇵 Keita (JP)"),
        ("ja-JP-NanamiNeural", "🇯🇵 Nanami (JP)"),
        ("zh-CN-XiaoxiaoNeural", "🇨🇳 Xiaoxiao (CN)"),
        ("zh-CN-YunxiNeural", "🇨🇳 Yunxi (CN)"),
        ("ko-KR-SunHiNeural", "🇰🇷 Sun-Hi (KR)"),
        ("ko-KR-InJoonNeural", "🇰🇷 InJoon (KR)"),
        ("fr-FR-DeniseNeural", "🇫🇷 Denise (FR)"),
        ("fr-FR-HenriNeural", "🇫🇷 Henri (FR)"),
            
        ("de-DE-KatjaNeural", "🇩🇪 Katja (DE)"),
        ("de-DE-ConradNeural", "🇩🇪 Conrad (DE)"),
        ("es-ES-AlvaroNeural", "🇪🇸 Álvaro (ES)"),
        ("es-ES-ElviraNeural", "🇪🇸 Elvira (ES)"),
        ("pt-BR-FranciscaNeural", "🇧🇷 Francisca (BR)"),
        ("pt-BR-AntonioNeural", "🇧🇷 Antônio (BR)"),
        ("it-IT-IsabellaNeural", "🇮🇹 Isabella (IT)"),
        ("it-IT-DiegoNeural", "🇮🇹 Diego (IT)"),
        ("ru-RU-SvetlanaNeural", "🇷🇺 Svetlana (RU)"),
        ("ru-RU-DmitryNeural", "🇷🇺 Dmitry (RU)"),
    ];

    protected override void OnInitialized()
    {
        var idx = Array.FindIndex(Environment.GetCommandLineArgs(), a =>
            a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < Environment.GetCommandLineArgs().Length)
        {
            
            // Could extract base URL if needed for other things
        }
    }

    private async Task Synthesize()
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        busy = true;
        error = null;
        audioDataUrl = null;
        audioSizeBytes = null;
        wordBoundaries = [];
        StateHasChanged();

        try
        {
            var lang = voice[..5]; // "en-US" from "en-US-..."

            var result = await EdgeTts.SynthesizeAsync(
                text,
                voice,
                lang,
                rate,
                CancellationToken.None
            );

            audioSizeBytes = result.Audio.Length;
            wordBoundaries = [.. result.WordBoundaries];
            audioDataUrl = $"data:audio/mpeg;base64,{Convert.ToBase64String(result.Audio)}";
        }
        catch (EdgeTtsException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            error = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            busy = false;
        }
    }
}
