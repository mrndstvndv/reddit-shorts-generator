using BlazorApp1.Services;
using SkiaSharp;

namespace BlazorApp1.IntegrationTests;

/// <summary>
/// Tests for the Reddit title card generator.
/// These validate the PNG output so when migrating to the new SkiaSharp API
/// (SKPathBuilder, SKSamplingOptions, etc.), regressions are caught.
/// </summary>
public class RedditTitleCardTests
{
    private static RedditService CreateService()
    {
        var factory = new MinimalHttpClientFactory();
        return new RedditService(factory);
    }

    /// <summary>
    /// Returns a valid PNG with the correct dimensions.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_ReturnsPngWithCorrectDimensions()
    {
        var service = CreateService();

        var bytes = await service.GenerateTitleCard(
            title: "This is a test title card for regression testing.",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: false
        );

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Validate PNG signature
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]); // P
        Assert.Equal(0x4E, bytes[2]); // N
        Assert.Equal(0x47, bytes[3]); // G

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(1920, bitmap.Height);
    }

    /// <summary>
    /// Light theme variant still produces a valid 1080x1920 PNG.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_LightTheme_ReturnsValidPng()
    {
        var service = CreateService();

        var bytes = await service.GenerateTitleCard(
            title: "Light theme card",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: true
        );

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(1920, bitmap.Height);
    }

    /// <summary>
    /// The card is vertically centered. We find its top by scanning from center outward,
    /// then verify the dark theme background color.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_DarkTheme_CardBackgroundIsCorrect()
    {
        var service = CreateService();

        var bytes = await service.GenerateTitleCard(
            title: "Check background color",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: false
        );

        using var bitmap = SKBitmap.Decode(bytes);
        var (cardY, _) = FindCardBounds(bitmap);

        var cardPixel = bitmap.GetPixel(540, cardY + 5);
        Assert.Equal(15, cardPixel.Red);
        Assert.Equal(20, cardPixel.Green);
        Assert.Equal(22, cardPixel.Blue);
    }

    /// <summary>
    /// Light theme card should have white background in the card area.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_LightTheme_CardBackgroundIsWhite()
    {
        var service = CreateService();

        var bytes = await service.GenerateTitleCard(
            title: "Check light background",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: true
        );

        using var bitmap = SKBitmap.Decode(bytes);
        var (cardY, _) = FindCardBounds(bitmap);

        var cardPixel = bitmap.GetPixel(540, cardY + 5);
        Assert.Equal(255, cardPixel.Red);
        Assert.Equal(255, cardPixel.Green);
        Assert.Equal(255, cardPixel.Blue);
    }

    /// <summary>
    /// Long title wraps and the card height grows accordingly.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_LongTitle_WrapsText()
    {
        var service = CreateService();

        var shortBytes = await service.GenerateTitleCard(
            title: "Short",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: false
        );

        var longBytes = await service.GenerateTitleCard(
            title: "This is a very long title that should definitely wrap to multiple " +
                   "lines because the card width cannot fit this many characters in " +
                   "a single line at the configured font size of 38",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: false
        );

        using var shortBmp = SKBitmap.Decode(shortBytes);
        using var longBmp = SKBitmap.Decode(longBytes);

        var (_, shortCardH) = FindCardBounds(shortBmp);
        var (_, longCardH) = FindCardBounds(longBmp);

        Assert.True(longCardH > shortCardH,
            $"Long title card height ({longCardH}) should exceed short title card height ({shortCardH})");
    }

    /// <summary>
    /// Emoji in titles should render without crashing (regression guard).
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_EmojiInTitle_DoesNotCrash()
    {
        var service = CreateService();

        var bytes = await service.GenerateTitleCard(
            title: "Testing emoji 🎉 in title 🔥 should work",
            subreddit: "r/test",
            subredditIconPath: "",
            isLightTheme: false
        );

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(1920, bitmap.Height);
    }

    /// <summary>
    /// Custom stats produce a different image than default stats.
    /// </summary>
    [Fact]
    public async Task GenerateTitleCard_CustomVotesAndComments_RendersDifferently()
    {
        var service = CreateService();

        var defaultBytes = await service.GenerateTitleCard(
            title: "Check stats",
            subreddit: "r/test",
            subredditIconPath: ""
        );

        var customBytes = await service.GenerateTitleCard(
            title: "Check stats",
            subreddit: "r/test",
            subredditIconPath: "",
            votes: "42",
            comments: "7",
            timeAgo: "5h ago",
            username: "testuser"
        );

        Assert.False(
            defaultBytes.AsSpan().SequenceEqual(customBytes.AsSpan()),
            "Custom stats should produce a different image than default stats"
        );
    }

    /// <summary>
    /// Scans the vertical center column of the bitmap for the card bounding box.
    /// The card is a non-transparent rounded rectangle centered horizontally.
    /// Returns (top, height) of the card area.
    /// </summary>
    private static (int Top, int Height) FindCardBounds(SKBitmap bitmap)
    {
        int centerX = bitmap.Width / 2;
        int top = 0, bottom = bitmap.Height;

        // Scan from top down to find first non-transparent pixel
        for (int y = 0; y < bitmap.Height; y++)
        {
            var pixel = bitmap.GetPixel(centerX, y);
            if (pixel.Alpha > 0)
            {
                top = y;
                break;
            }
        }

        // Scan from bottom up to find last non-transparent pixel
        for (int y = bitmap.Height - 1; y >= 0; y--)
        {
            var pixel = bitmap.GetPixel(centerX, y);
            if (pixel.Alpha > 0)
            {
                bottom = y;
                break;
            }
        }

        return (top, bottom - top + 1);
    }
}

file class MinimalHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name = "") => new();
}
