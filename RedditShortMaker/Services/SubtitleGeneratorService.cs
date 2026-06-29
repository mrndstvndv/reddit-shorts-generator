//
// SubtitleGeneratorService — converts Edge TTS WordBoundary data into ASS subtitle files.
//
// Each word from the TTS stream becomes a Dialogue line with precise start/end timing
// derived from the word boundary's Offset and Duration (100ns ticks).
//
// The output matches the style of the reference .ass file:
//   • Portrait layout (1080×1920)
//   • "Arial" font (word-by-word karaoke-style)
//   • White fill with black outline, center-aligned
//   • Every subtitle shows exactly one word at its spoken interval
//

using System.Text;

namespace RedditShortMaker.Services;

public record AssDialogue(
    int Layer,
    double StartSec,
    double EndSec,
    string Style,
    string Text
);

public class SubtitleGeneratorService
{
    /// <summary>
    /// Generates an ASS subtitle string from word boundaries.
    /// </summary>
    public string Generate(IReadOnlyList<WordBoundary> boundaries, AssStyle? style = null)
    {
        return Generate(boundaries, 0.0, style);
    }

    /// <summary>
    /// Generates an ASS subtitle string from word boundaries, shifted by an offset in seconds.
    /// </summary>
    public string Generate(IReadOnlyList<WordBoundary> boundaries, double offsetSec, AssStyle? style = null)
    {
        style ??= AssStyle.Default;

        if (boundaries.Count == 0)
            return MinimalAssFile(style);

        var dialogs = BuildDialogues(boundaries, offsetSec, style);
        return BuildAssFile(style, dialogs);
    }

    /// <summary>
    /// Saves an ASS subtitle file to disk.
    /// </summary>
    public Task SaveAsync(string path, IReadOnlyList<WordBoundary> boundaries, AssStyle? style = null, CancellationToken ct = default)
    {
        return SaveAsync(path, boundaries, 0.0, style, ct);
    }

    /// <summary>
    /// Saves an ASS subtitle file to disk, shifted by an offset in seconds.
    /// </summary>
    public async Task SaveAsync(string path, IReadOnlyList<WordBoundary> boundaries, double offsetSec, AssStyle? style = null, CancellationToken ct = default)
    {
        var ass = Generate(boundaries, offsetSec, style);
        await File.WriteAllTextAsync(path, ass, ct);
    }

    // ─── Dialog building ───────────────────────────────────────────

    private static List<AssDialogue> BuildDialogues(IReadOnlyList<WordBoundary> boundaries, double offsetSec, AssStyle style)
    {
        var dialogs = new List<AssDialogue>(boundaries.Count);

        for (int i = 0; i < boundaries.Count; i++)
        {
            var wb = boundaries[i];
            var start = wb.StartSec + offsetSec;
            var end = wb.EndSec + offsetSec;

            // Ensure minimum visible duration so words don't flash
            if (end - start < style.MinDurationSec)
                end = start + style.MinDurationSec;

            // Prevent overlapping — if this word ends after the next starts, clamp strictly
            if (i + 1 < boundaries.Count)
            {
                var nextStart = boundaries[i + 1].StartSec + offsetSec;
                if (end > nextStart)
                    end = nextStart;
            }

            var text = wb.Text;
            if (style.Uppercase)
                text = text.ToUpperInvariant();

            dialogs.Add(new AssDialogue(
                Layer: 0,
                StartSec: start,
                EndSec: end,
                Style: style.Name,
                Text: EscapeAssText(text)
            ));
        }

        return dialogs;
    }

    // ─── ASS file assembly ──────────────────────────────────────────

    private static string BuildAssFile(AssStyle style, List<AssDialogue> dialogs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {style.PlayResX}");
        sb.AppendLine($"PlayResY: {style.PlayResY}");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine(FormatStyleLine(style));
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var d in dialogs)
        {
            sb.Append($"Dialogue: {d.Layer},");
            sb.Append($"{TimeStr(d.StartSec)},");
            sb.Append($"{TimeStr(d.EndSec)},");
            sb.Append($"{d.Style},");
            sb.Append(",0,0,0,,");
            sb.AppendLine(d.Text);
        }

        return sb.ToString();
    }

    private static string MinimalAssFile(AssStyle style)
    {
        return $"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: {style.PlayResX}
            PlayResY: {style.PlayResY}
            ScaledBorderAndShadow: yes

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            {FormatStyleLine(style)}

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text

            """;
    }

    // ─── Formatting helpers ─────────────────────────────────────────

    private static string FormatStyleLine(AssStyle s)
    {
        return $"Style: {s.Name},{s.FontName},{s.FontSize}," +
               $"{ColorToAss(s.PrimaryColor)},{ColorToAss(s.SecondaryColor)}," +
               $"{ColorToAss(s.OutlineColor)},{ColorToAss(s.BackColor)}," +
               $"{(s.Bold ? 1 : 0)},{(s.Italic ? 1 : 0)},0,0," +
               $"{s.ScaleX},{s.ScaleY},{s.Spacing},{s.Angle}," +
               $"{(int)s.BorderStyle},{s.OutlineSize},{s.ShadowSize}," +
               $"{(int)s.Alignment},{s.MarginL},{s.MarginR},{s.MarginV}," +
               $"{(int)s.Encoding}";
    }

    /// <summary>
    /// Converts seconds to ASS time format (H:MM:SS.cc).
    /// </summary>
    private static string TimeStr(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var totalMs = (int)(seconds * 100);
        var h = totalMs / 360_000;
        totalMs %= 360_000;
        var m = totalMs / 6_000;
        totalMs %= 6_000;
        var s = totalMs / 100;
        var cs = totalMs % 100;
        return $"{h}:{m:D2}:{s:D2}.{cs:D2}";
    }

    /// <summary>
    /// Convert #RRGGBB or #AARRGGBB to ASS format (AABBGGRR).
    /// ASS stores colours as &H AABBGGRR.
    /// </summary>
    private static string ColorToAss(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));

        // If no alpha, default to &H00
        byte a = hex.Length >= 8 ? byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber) : (byte)0;
        byte r = byte.Parse(hex[^6..^4], System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(hex[^4..^2], System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(hex[^2..], System.Globalization.NumberStyles.HexNumber);

        return $"&H{a:X2}{b:X2}{g:X2}{r:X2}";
    }

    /// <summary>
    /// Escape ASS-sensitive characters in text.
    /// ASS uses {} for override tags and \n for newlines.
    /// </summary>
    private static string EscapeAssText(string text)
    {
        // Replace curly braces which are ASS override tags
        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}");
    }
}

// ─── Style configuration ────────────────────────────────────────────

public class AssStyle
{
    public string Name { get; init; } = "Default";
    public string FontName { get; init; } = "Arial";
    public int FontSize { get; init; } = 123;
    public string PrimaryColor { get; init; } = "#FFFFFF";
    public string SecondaryColor { get; init; } = "#0000FF";
    public string OutlineColor { get; init; } = "#000000";
    public string BackColor { get; init; } = "#000000";
    public bool Bold { get; init; } = true;
    public bool Italic { get; init; } = false;
    public double ScaleX { get; init; } = 100;
    public double ScaleY { get; init; } = 100;
    public double Spacing { get; init; } = 0;
    public double Angle { get; init; } = 0;
    public AssBorderStyle BorderStyle { get; init; } = AssBorderStyle.Outline;
    public double OutlineSize { get; init; } = 3.0;
    public double ShadowSize { get; init; } = 0;
    public AssAlignment Alignment { get; init; } = AssAlignment.Center;
    public int MarginL { get; init; } = 10;
    public int MarginR { get; init; } = 10;
    public int MarginV { get; init; } = 10;
    public AssEncoding Encoding { get; init; } = AssEncoding.Default;
    public int PlayResX { get; init; } = 1080;
    public int PlayResY { get; init; } = 1920;

    /// <summary>
    /// Minimum duration for any subtitle (in seconds).
    /// Prevents words from flashing too fast to read.
    /// </summary>
    public double MinDurationSec { get; init; } = 0.15;

    /// <summary>
    /// Whether to uppercase all text (matches the reference style).
    /// </summary>
    public bool Uppercase { get; init; } = true;

    public static AssStyle Default => new();
}

// ─── Enums ──────────────────────────────────────────────────────────

public enum AssBorderStyle
{
    Outline = 1,
    OpaqueBox = 3,
}

public enum AssAlignment
{
    BottomLeft = 1,
    BottomCenter = 2,
    BottomRight = 3,
    MiddleLeft = 4,
    Center = 5,
    MiddleRight = 6,
    TopLeft = 7,
    TopCenter = 8,
    TopRight = 9,
}

public enum AssEncoding
{
    Default = 1,
    Unicode = 1,
    Western = 0,
}
