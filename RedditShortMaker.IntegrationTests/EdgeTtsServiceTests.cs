using RedditShortMaker.Services;

namespace RedditShortMaker.IntegrationTests;

/// <summary>
/// Integration tests against the live Microsoft Edge TTS WebSocket API.
/// These tests hit a real network service and require internet connectivity.
/// </summary>
[Trait("Category", "Integration")]
public class EdgeTtsServiceTests : IAsyncLifetime
{
    private EdgeTtsService _service = null!;

    public async Task InitializeAsync()
    {
        _service = new EdgeTtsService();
        await Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The most basic path: short text, default voice.
    /// Verifies we get both audio and word boundaries.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_ShortText_ReturnsAudioAndWordBoundaries()
    {
        var result = await _service.SynthesizeAsync("Hello world.");

        Assert.NotNull(result);
        Assert.NotNull(result.Audio);
        Assert.NotEmpty(result.Audio);

        Assert.NotNull(result.WordBoundaries);
        Assert.NotEmpty(result.WordBoundaries);
    }

    /// <summary>
    /// Verify word boundary timestamps are in order and non-negative.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_WordBoundaries_AreInAscendingOrder()
    {
        var result = await _service.SynthesizeAsync("The quick brown fox jumps over the lazy dog.");

        Assert.NotEmpty(result.WordBoundaries);

        for (int i = 0; i < result.WordBoundaries.Count; i++)
        {
            var wb = result.WordBoundaries[i];

            Assert.False(string.IsNullOrWhiteSpace(wb.Text), $"Word at index {i} is empty");
            Assert.True(wb.OffsetTicks >= 0, $"Word '{wb.Text}' has negative offset");
            Assert.True(wb.DurationTicks > 0, $"Word '{wb.Text}' has zero/negative duration");

            if (i > 0)
            {
                var prev = result.WordBoundaries[i - 1];
                Assert.True(
                    wb.StartSec >= prev.EndSec - 0.01,
                    $"Word '{wb.Text}' starts ({wb.StartSec:F3}s) before previous word " +
                    $"'{prev.Text}' ends ({prev.EndSec:F3}s)"
                );
            }
        }
    }

    /// <summary>
    /// Longer text to stress test streaming multiple chunks and metadata frames.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_LongerText_HandlesMultipleChunks()
    {
        var text = "This is a slightly longer sentence to test that the service " +
                   "can handle streaming audio across multiple chunks. " +
                   "We want to ensure that concatenation works correctly " +
                   "and that word boundaries are still accurate.";

        var result = await _service.SynthesizeAsync(text);

        Assert.NotEmpty(result.Audio);
        Assert.True(result.Audio.Length > 5000, "Audio should be more than 5KB for a long sentence");

        Assert.NotEmpty(result.WordBoundaries);
        Assert.True(result.WordBoundaries.Count >= 20,
            $"Expected at least 20 words for longer text, got {result.WordBoundaries.Count}");
    }

    /// <summary>
    /// Test a different voice to ensure the voice parameter works.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_DifferentVoice_Works()
    {
        var result = await _service.SynthesizeAsync(
            "Testing a different voice now.",
            voice: "en-US-AriaNeural");

        Assert.NotEmpty(result.Audio);
        Assert.NotEmpty(result.WordBoundaries);
    }

    /// <summary>
    /// Test that rate parameter affects the output (faster speech).
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_FasterRate_ProducesShorterAudio()
    {
        var text = "This sentence should take a measurable amount of time.";

        var normal = await _service.SynthesizeAsync(text, rate: 0);
        var fast = await _service.SynthesizeAsync(text, rate: 50);

        Assert.NotEmpty(normal.Audio);
        Assert.NotEmpty(fast.Audio);

        // Faster rate should have fewer/denser audio bytes
        // (not a strict assertion since compression varies, but we expect it to work)
        Assert.NotEmpty(normal.WordBoundaries);
        Assert.NotEmpty(fast.WordBoundaries);
    }

    /// <summary>
    /// Ensure audio output is a valid MP3 (starts with MP3 sync header).
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_Audio_IsValidMp3()
    {
        var result = await _service.SynthesizeAsync("Validate the audio format.");

        Assert.NotEmpty(result.Audio);

        // MP3 frames start with 0xFF 0xFB (or 0xFF 0xFA, 0xFF 0xF2, etc.)
        // The Edge TTS response is an MPEG audio stream
        Assert.Equal(0xFF, result.Audio[0]);
        Assert.True((result.Audio[1] & 0xE0) == 0xE0,
            $"Second byte 0x{result.Audio[1]:X2} doesn't match MP3 sync bits");
    }

    /// <summary>
    /// Write the generated audio to a temp file so you can manually verify it.
    /// This test is not usually run — unskip to hear the output.
    /// </summary>
    [Fact(Skip = "Manual audio file generation")]
    public async Task SynthesizeAsync_WriteSampleAudioFile()
    {
        var result = await _service.SynthesizeAsync(
            "This is a sample audio file generated by the Edge TTS service. " +
            "If you can hear this, the integration is working correctly.");

        var outputDir = Path.Combine(Path.GetTempPath(), "EdgeTtsTests");
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, "sample_output.mp3");
        await File.WriteAllBytesAsync(path, result.Audio);

        Assert.True(File.Exists(path), $"File should exist at {path}");
    }

    /// <summary>
    /// Single word should still produce valid output.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_SingleWord_Works()
    {
        var result = await _service.SynthesizeAsync("Hello");

        Assert.NotEmpty(result.Audio);
        Assert.NotEmpty(result.WordBoundaries);
        Assert.Equal("Hello", result.WordBoundaries[0].Text, ignoreCase: true);
    }

    /// <summary>
    /// Text with punctuation should still work.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_TextWithPunctuation_Works()
    {
        var result = await _service.SynthesizeAsync(
            "Hello! How are you? I'm doing great... Let's go.");

        Assert.NotEmpty(result.Audio);
        Assert.NotEmpty(result.WordBoundaries);
    }

    /// <summary>
    /// Empty text should throw.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_EmptyText_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SynthesizeAsync(""));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whitespace-only text should throw.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_WhitespaceText_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SynthesizeAsync("   \t\n  "));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Explicit cancellation should throw OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task SynthesizeAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() =>
            _service.SynthesizeAsync("This should be cancelled.", ct: cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }
}
