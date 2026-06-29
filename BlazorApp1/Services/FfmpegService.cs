using System.Diagnostics;

namespace BlazorApp1.Services;

public class FfmpegException(string message, Exception? inner = null)
    : Exception(message, inner);

public record CompositionOptions
{
    public required string VideoPath { get; init; }
    public required string TitleAudioPath { get; init; }
    public required string BodyAudioPath { get; init; }
    public required string TitleImagePath { get; init; }
    public required string SubtitlePath { get; init; }
    public required string OutputPath { get; init; }
    public double TitleDurationSec { get; init; }
    public double? BgStartTimeSec { get; init; }
    public string? FontsDir { get; init; }
    public bool DeleteInputs { get; init; } = true;
}

public class FfmpegService
{
    private string? _ffmpegPath;
    private bool _validated;
    private readonly object _lock = new();

    public async Task ComposeAsync(CompositionOptions options, CancellationToken ct = default)
    {
        var ffmpeg = ResolveFfmpeg();

        var bgDuration = await ProbeDurationAsync(ffmpeg, options.VideoPath, ct);
        var titleDuration = options.TitleDurationSec > 0
            ? options.TitleDurationSec
            : await ProbeDurationAsync(ffmpeg, options.TitleAudioPath, ct);

        var bgStart = options.BgStartTimeSec ?? PickRandomStart(bgDuration);

        var args = BuildArgs(options, bgStart, titleDuration);
        await RunFfmpegAsync(ffmpeg, args, ct);

        if (options.DeleteInputs)
        {
            TryDelete(options.TitleAudioPath);
            TryDelete(options.BodyAudioPath);
            TryDelete(options.TitleImagePath);
            TryDelete(options.SubtitlePath);
        }
    }

    // ─── FFmpeg path resolution ────────────────────────────────────

    private string ResolveFfmpeg()
    {
        if (_validated) return _ffmpegPath!;

        lock (_lock)
        {
            if (_validated) return _ffmpegPath!;

            var which = OperatingSystem.IsWindows() ? "where" : "which";
            var psi = new ProcessStartInfo(which, "ffmpeg")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            string? path;
            try
            {
                using var proc = Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                path = output.Trim().Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            }
            catch (Exception ex)
            {
                throw new FfmpegException("ffmpeg not found in PATH.", ex);
            }

            if (string.IsNullOrEmpty(path))
                throw new FfmpegException("ffmpeg not found in PATH.");

            var versionPsi = new ProcessStartInfo(path, "-version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            try
            {
                using var proc = Process.Start(versionPsi)!;
                var version = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                if (!version.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
                    throw new FfmpegException($"ffmpeg binary at {path} is not valid");
            }
            catch (Exception ex) when (ex is not FfmpegException)
            {
                throw new FfmpegException($"Failed to validate ffmpeg at {path}: {ex.Message}", ex);
            }

            _ffmpegPath = path;
            _validated = true;
            return path;
        }
    }

    // ─── Probe ──────────────────────────────────────────────────────

    private static async Task<double> ProbeDurationAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            path,
        };

        var psi = new ProcessStartInfo(ffprobePath(ffmpeg))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return double.TryParse(output.Trim(), out var dur) ? dur : 0.0;
    }

    private static string ffprobePath(string ffmpeg) =>
        OperatingSystem.IsWindows()
            ? ffmpeg.Replace("ffmpeg.exe", "ffprobe.exe")
            : ffmpeg.Replace("ffmpeg", "ffprobe");

    private static double PickRandomStart(double bgDuration) =>
        bgDuration > 10 ? Random.Shared.NextDouble() * (bgDuration - 5) : 0;

    // ─── Argument builder ───────────────────────────────────────────

    private static List<string> BuildArgs(CompositionOptions options, double bgStart, double titleDuration)
    {
        var subsEscaped = EscapeFilterPath(options.SubtitlePath);
        var fontsEscaped = options.FontsDir is not null ? EscapeFilterPath(options.FontsDir) : null;

        var subtitlesFilter = fontsEscaped is not null
            ? $"subtitles=f={subsEscaped}:fontsdir={fontsEscaped}"
            : $"subtitles=f={subsEscaped}";

        var filter = $"[0:v][1:v]concat=n=2:v=1:a=0[bg_v];" +
                     $"[3:a][4:a]concat=n=2:v=0:a=1[a];" +
                     $"[bg_v][2:v]overlay=(W-w)/2:(H-h)/2:enable='between(t,0,{titleDuration})'[v1];" +
                     $"[v1]{subtitlesFilter}[v]";

        var args = new List<string>
        {
            "-y",
            "-ss", $"{bgStart:F3}",
            "-i", options.VideoPath,
            "-stream_loop", "-1",
            "-i", options.VideoPath,
            "-i", options.TitleImagePath,
            "-i", options.TitleAudioPath,
            "-i", options.BodyAudioPath,
            "-filter_complex", filter,
            "-map", "[v]",
            "-map", "[a]",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "18",
            "-c:a", "aac",
            "-b:a", "192k",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            "-shortest",
            options.OutputPath,
        };

        return args;
    }

    // ─── Execution ───────────────────────────────────────────────────

    private static async Task RunFfmpegAsync(string ffmpeg, List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = new Process { StartInfo = psi };

        var stderr = new StringWriter();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.WriteLine(e.Data);
        };

        proc.Start();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            var error = stderr.ToString();
            throw new FfmpegException($"ffmpeg exited with code {proc.ExitCode}.\n{TrimStderr(error)}");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static string EscapeFilterPath(string path)
    {
        // Make absolute and escape colons (filter option separators)
        var abs = Path.GetFullPath(path);
        return abs.Replace(":", "\\:");
    }

    private static string TrimStderr(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = lines.Where(l =>
            l.StartsWith("Error") ||
            l.StartsWith("Invalid") ||
            l.StartsWith("Unable") ||
            l.Contains("not found") ||
            l.Contains("no such") ||
            l.Contains("failed", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        return filtered.Count > 0
            ? string.Join('\n', filtered.TakeLast(20))
            : string.Join('\n', lines.TakeLast(20));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
