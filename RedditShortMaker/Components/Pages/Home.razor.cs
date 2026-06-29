using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.JSInterop;
using RedditShortMaker.Models;
using RedditShortMaker.Services;

namespace RedditShortMaker.Components.Pages;

public partial class Home
{
    [Inject]
    private RedditService Reddit { get; set; } = null!;

    [Inject]
    private RedditCardService CardService { get; set; } = null!;

    [Inject]
    private EdgeTtsService EdgeTts { get; set; } = null!;
    
    [Inject]
    private SubtitleGeneratorService SubGen { get; set; } = null!;
    
    [Inject]
    private FfmpegService Ffmpeg { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private IWebHostEnvironment Env { get; set; } = null!;

    public readonly record struct GenState
    {
        private readonly int _id;
        public string? ErrorMessage { get; }

        private GenState(int id, string? errorMessage = null)
        {
            _id = id;
            ErrorMessage = errorMessage;
        }

        public static readonly GenState Idle = new(0);
        public static readonly GenState Fetching = new(1);
        public static readonly GenState GeneratingTitleCard = new(2);
        public static readonly GenState SynthesizingTitleVoice = new(3);
        public static readonly GenState SynthesizingBodyVoice = new(4);
        public static readonly GenState GeneratingVideo = new(5);
        public static readonly GenState Complete = new(6);

        public static GenState Error(string message) => new(7, message);
    }

    private string url = "";
    private string subredditName = "r/buhaydigital";
    private string subredditIconPath = "";
    private string username = "username";
    private string votes = "4.1K";
    private string comments = "2.2K";
    private string timeAgo = "2d ago";
    private bool isLightTheme;
    private string cardThemeSelection = "dark";
    private string? identifier;
    private GenState state = GenState.Idle;
    private PostData? post;
    private string postTitle = "";
    private string postBody = "";

    private readonly string FontsDir = "./fonts";
    private readonly string VidsDir = "./vids";
    
    private List<string> availableFonts = new();
    private List<string> availableGameplays = new();
    
    private string selectedFont = "Arial";
    private string selectedGameplay = "";
    
    private string? fontUploadError;
    private string? gameplayUploadError;
    private bool isUploadingFont;
    private bool isUploadingGameplay;

    protected override void OnInitialized()
    {
        try
        {
            Directory.CreateDirectory(FontsDir);
            Directory.CreateDirectory(VidsDir);
        }
        catch
        {
            // Ignore
        }
        RefreshFiles();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var storedFont = await JS.InvokeAsync<string>("localStorage.getItem", "reddit_generator_font");
                var storedGameplay = await JS.InvokeAsync<string>("localStorage.getItem", "reddit_generator_gameplay");
                var storedTheme = await JS.InvokeAsync<string>("localStorage.getItem", "reddit_generator_theme");

                var changed = false;
                if (!string.IsNullOrEmpty(storedFont) && (storedFont == "Arial" || availableFonts.Contains(storedFont)))
                {
                    selectedFont = storedFont;
                    changed = true;
                }
                if (!string.IsNullOrEmpty(storedGameplay) && availableGameplays.Contains(storedGameplay))
                {
                    selectedGameplay = storedGameplay;
                    changed = true;
                }
                if (!string.IsNullOrEmpty(storedTheme))
                {
                    cardThemeSelection = storedTheme;
                    isLightTheme = (cardThemeSelection == "light");
                    changed = true;
                }

                if (changed)
                {
                    StateHasChanged();
                }
            }
            catch
            {
                // LocalStorage not available or failed during prerendering
            }
        }
    }

    private void RefreshFiles()
    {
        try
        {
            availableFonts = Directory.Exists(FontsDir)
                ? Directory.GetFiles(FontsDir)
                    .Select(Path.GetFileName)
                    .Where(f => f != null && (f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                    .Select(f => f!)
                    .ToList()
                : new List<string>();
        }
        catch
        {
            availableFonts = new List<string>();
        }

        try
        {
            availableGameplays = Directory.Exists(VidsDir)
                ? Directory.GetFiles(VidsDir)
                    .Select(Path.GetFileName)
                    .Where(f => f != null && (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)))
                    .Select(f => f!)
                    .ToList()
                : new List<string>();
        }
        catch
        {
            availableGameplays = new List<string>();
        }

        if (string.IsNullOrEmpty(selectedGameplay) && availableGameplays.Count > 0)
        {
            selectedGameplay = availableGameplays.FirstOrDefault(g => g.Contains("mc-parkour", StringComparison.OrdinalIgnoreCase)) 
                               ?? availableGameplays.First();
        }
    }

    private async Task OnFontFileChange(InputFileChangeEventArgs e)
    {
        fontUploadError = null;
        isUploadingFont = true;
        StateHasChanged();

        try
        {
            var file = e.File;
            if (file != null)
            {
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf")
                {
                    fontUploadError = "Only .ttf and .otf font files are supported.";
                    return;
                }

                Directory.CreateDirectory(FontsDir);
                var targetPath = Path.Combine(FontsDir, file.Name);
                
                using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
                using var fileStream = File.Create(targetPath);
                await stream.CopyToAsync(fileStream);

                RefreshFiles();
                selectedFont = file.Name;
                await SaveFontAsync();
            }
        }
        catch (Exception ex)
        {
            fontUploadError = $"Upload failed: {ex.Message}";
        }
        finally
        {
            isUploadingFont = false;
            StateHasChanged();
        }
    }

    private async Task OnGameplayFileChange(InputFileChangeEventArgs e)
    {
        gameplayUploadError = null;
        isUploadingGameplay = true;
        StateHasChanged();

        try
        {
            var file = e.File;
            if (file != null)
            {
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                var allowed = new[] { ".mp4", ".mov", ".mkv", ".avi" };
                if (!allowed.Contains(ext))
                {
                    gameplayUploadError = "Only video files (.mp4, .mov, .mkv, .avi) are supported.";
                    return;
                }

                Directory.CreateDirectory(VidsDir);
                var targetPath = Path.Combine(VidsDir, file.Name);
                
                using var stream = file.OpenReadStream(maxAllowedSize: 800 * 1024 * 1024);
                using var fileStream = File.Create(targetPath);
                await stream.CopyToAsync(fileStream);

                RefreshFiles();
                selectedGameplay = file.Name;
                await SaveGameplayAsync();
            }
        }
        catch (Exception ex)
        {
            gameplayUploadError = $"Upload failed: {ex.Message}";
        }
        finally
        {
            isUploadingGameplay = false;
            StateHasChanged();
        }
    }

    private async Task SaveFontAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "reddit_generator_font", selectedFont);
        }
        catch
        {
            // Ignore
        }
    }

    private async Task SaveGameplayAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "reddit_generator_gameplay", selectedGameplay);
        }
        catch
        {
            // Ignore
        }
    }

    private async Task SaveThemeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "reddit_generator_theme", cardThemeSelection);
        }
        catch
        {
            // Ignore
        }
    }

    private async Task HandleUrlKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await GetPost();
        }
    }

    private async Task GetPost()
    {
        state = GenState.Fetching;
        post = null;
        postTitle = "";
        postBody = "";
        username = "username";
        StateHasChanged();

        try
        {
            post = await Reddit.GetPost(url);
            postTitle = post.Title;
            postBody = post.Body;
            username = post.Author;
            votes = post.Votes;
            comments = post.Comments;
            timeAgo = post.TimeAgo;
            ExtractSubredditFromUrl();
            state = GenState.Idle;
        }
        catch (RedditException ex)
        {
            state = GenState.Error(ex.Message);
        }
    }

    private void ExtractSubredditFromUrl()
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    subredditName = $"r/{segments[i + 1]}";
                    return;
                }
            }
        }
        catch
        {
            // Keep default
        }
    }

    private string GetButtonLabel()
    {
        if (state.ErrorMessage is not null) return "Retry";
        if (state == GenState.Fetching) return "Fetching...";
        if (state == GenState.GeneratingTitleCard) return "Generating...";
        if (state == GenState.SynthesizingTitleVoice) return "Synthesizing...";
        if (state == GenState.SynthesizingBodyVoice) return "Synthesizing...";
        if (state == GenState.GeneratingVideo) return "Rendering...";
        if (state == GenState.Complete) return "Generate Again";
        return "Generate Video";
    }

    private async Task GetVideo()
    {
        if (post is null) return;
        if (string.IsNullOrEmpty(selectedGameplay))
        {
            state = GenState.Error("Please select or upload a gameplay video first.");
            return;
        }

        isLightTheme = (cardThemeSelection == "light");
        state = GenState.GeneratingTitleCard;
        StateHasChanged();

        try
        {
            var titleCard = await CardService.GenerateTitleCard(
                postTitle,
                subredditName,
                subredditIconPath,
                isLightTheme,
                votes,
                comments,
                timeAgo,
                username
            );

            identifier = new Uri(post.OldUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
            var dir = Path.Combine(Env.ContentRootPath, "outputs", identifier);
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "title-card.png"), titleCard);

            state = GenState.SynthesizingTitleVoice;
            StateHasChanged();

            var titleResult = await EdgeTts.SynthesizeAsync(postTitle);
            var titleVoicePath = Path.Combine(dir, "title-voice.mp3");
            await File.WriteAllBytesAsync(titleVoicePath, titleResult.Audio);
            
            state = GenState.SynthesizingBodyVoice;
            StateHasChanged();
            
            var bodyResult = await EdgeTts.SynthesizeAsync(postBody);
            await File.WriteAllBytesAsync(Path.Combine(dir, "body-voice.mp3"), bodyResult.Audio);

            // Probe title audio duration and shift body subtitles by it
            var titleDuration = await Ffmpeg.GetDurationAsync(titleVoicePath);

            var selectedFontName = "Arial";
            var hasCustomFont = false;
            if (selectedFont != "Arial" && !string.IsNullOrEmpty(selectedFont))
            {
                hasCustomFont = true;
                var fontPath = Path.Combine(FontsDir, selectedFont);
                if (File.Exists(fontPath))
                {
                    try
                    {
                        using var tf = SkiaSharp.SKTypeface.FromFile(fontPath);
                        if (tf is not null && !string.IsNullOrEmpty(tf.FamilyName))
                        {
                            selectedFontName = tf.FamilyName;
                        }
                        else
                        {
                            selectedFontName = Path.GetFileNameWithoutExtension(selectedFont);
                        }
                    }
                    catch
                    {
                        selectedFontName = Path.GetFileNameWithoutExtension(selectedFont);
                    }
                }
                else
                {
                    selectedFontName = Path.GetFileNameWithoutExtension(selectedFont);
                }
            }

            var assStyle = new AssStyle
            {
                FontName = selectedFontName
            };

            await SubGen.SaveAsync(Path.Combine(dir, "subs.ass"), bodyResult.WordBoundaries, titleDuration, assStyle);

            state = GenState.GeneratingVideo;
            StateHasChanged();

            var gameplayVideoPath = Path.Combine(VidsDir, selectedGameplay);
            if (!File.Exists(gameplayVideoPath))
            {
                throw new FileNotFoundException($"Selected gameplay video file '{selectedGameplay}' was not found in './vids'.");
            }

            await Ffmpeg.ComposeAsync(new CompositionOptions
            {
                VideoPath = gameplayVideoPath,
                TitleAudioPath = titleVoicePath,
                BodyAudioPath = Path.Combine(dir, "body-voice.mp3"),
                TitleImagePath = Path.Combine(dir, "title-card.png"),
                SubtitlePath = Path.Combine(dir, "subs.ass"),
                OutputPath = Path.Combine(dir, $"{identifier}.mp4"),
                TitleDurationSec = titleDuration,
                FontsDir = hasCustomFont ? FontsDir : null
            });

            state = GenState.Complete;
        }
        catch (Exception ex)
        {
            state = GenState.Error($"Failed to generate video: {ex.Message}\n\nDetails:\n{ex}");
        }
    }
}
