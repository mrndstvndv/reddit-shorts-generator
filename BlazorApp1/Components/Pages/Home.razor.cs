using Microsoft.AspNetCore.Components;
using BlazorApp1.Models;
using BlazorApp1.Services;

namespace BlazorApp1.Components.Pages;

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

    private string url = "https://www.reddit.com/r/buhaydigital/comments/1uf64ez/wfh_things_that_make_me_feel_rich_for_no_reason/";
    private string subredditName = "r/buhaydigital";
    private string subredditIconPath = "";
    private string username = "username";
    private string votes = "4.1K";
    private string comments = "2.2K";
    private string timeAgo = "2d ago";
    private bool isLightTheme;
    private string? identifier;
    private GenState state = GenState.Idle;
    private PostData? post;
    private string postTitle = "";

    private async Task GetPost()
    {
        state = GenState.Fetching;
        post = null;
        postTitle = "";
        username = "username";
        StateHasChanged();

        try
        {
            post = await Reddit.GetPost(url);
            postTitle = post.Title;
            username = post.Author;
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
            var dir = $"./outputs/{identifier}";
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "title-card.png"), titleCard);

            state = GenState.SynthesizingTitleVoice;
            StateHasChanged();

            var titleResult = await EdgeTts.SynthesizeAsync(post.Title);
            var titleVoicePath = Path.Combine(dir, "title-voice.mp3");
            await File.WriteAllBytesAsync(titleVoicePath, titleResult.Audio);
            
            state = GenState.SynthesizingBodyVoice;
            StateHasChanged();
            
            var bodyResult = await EdgeTts.SynthesizeAsync(post.Body);
            await File.WriteAllBytesAsync(Path.Combine(dir, "body-voice.mp3"), bodyResult.Audio);

            // Probe title audio duration and shift body subtitles by it
            var titleDuration = await Ffmpeg.GetDurationAsync(titleVoicePath);
            await SubGen.SaveAsync(Path.Combine(dir, "subs.ass"), bodyResult.WordBoundaries, titleDuration);

            state = GenState.GeneratingVideo;
            StateHasChanged();

            await Ffmpeg.ComposeAsync(new CompositionOptions
            {
                VideoPath = "./vids/mc-parkour-vertical.mp4",
                TitleAudioPath = titleVoicePath,
                BodyAudioPath = Path.Combine(dir, "body-voice.mp3"),
                TitleImagePath = Path.Combine(dir, "title-card.png"),
                SubtitlePath = Path.Combine(dir, "subs.ass"),
                OutputPath = Path.Combine(dir, $"{identifier}.mp4"),
                TitleDurationSec = titleDuration
            });

            state = GenState.Complete;
        }
        catch (Exception ex)
        {
            state = GenState.Error($"Failed to generate video: {ex.Message}");
        }
    }
}
