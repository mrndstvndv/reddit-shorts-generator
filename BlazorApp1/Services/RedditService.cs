using HtmlAgilityPack;
using SkiaSharp;

namespace BlazorApp1.Services;

public record PostData(string Title, string Body, string Author);

public class RedditException(string message, Exception? inner = null)
    : Exception(message, inner);

public class RedditPostNotFoundException(string url, string reason)
    : RedditException($"Could not find post content at {url}: {reason}");

public class RedditFetchException(string url, string reason, Exception inner)
    : RedditException($"Failed to fetch {url}: {reason}", inner);

public class RedditService(IHttpClientFactory clientFactory, ILogger<RedditService> logger)
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

        var postNode = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'thing') and contains(@data-type, 'link')]");

        var body = postNode?.SelectSingleNode(".//div[contains(@class, 'usertext-body')]//div[contains(@class, 'md')]")
            ?.InnerText.Trim();

        if (body is null)
            throw new RedditPostNotFoundException(url, "body not found");

        var author = postNode?.GetAttributeValue("data-author", "username") ?? "username";

        return new PostData(title, body, author);
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

    private SKTypeface GetTypeface(bool bold)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        
        string[] fontFamilies = { "Inter", "Helvetica Neue", "Helvetica", "Arial", "Segoe UI" };
        foreach (var family in fontFamilies)
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(family, style);
                if (tf != null && !tf.FamilyName.Equals("Portable User Interface", StringComparison.OrdinalIgnoreCase))
                {
                    return tf;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load typeface '{FontFamily}'", family);
            }
        }
        return SKTypeface.Default;
    }

    private static void DrawUpArrow(SKCanvas canvas, float x, float y, float size, bool isLightTheme)
    {
        using var paint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(87, 111, 118) : new SKColor(215, 218, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        
        float w = size * 0.8f;
        float h = size * 0.9f;

        using var builder = new SKPathBuilder();
        builder.MoveTo(new SKPoint(x, y - h / 2));
        builder.LineTo(new SKPoint(x - w / 2, y - h / 2 + w / 2));
        builder.LineTo(new SKPoint(x - w / 4, y - h / 2 + w / 2));
        builder.LineTo(new SKPoint(x - w / 4, y + h / 2));
        builder.LineTo(new SKPoint(x + w / 4, y + h / 2));
        builder.LineTo(new SKPoint(x + w / 4, y - h / 2 + w / 2));
        builder.LineTo(new SKPoint(x + w / 2, y - h / 2 + w / 2));
        builder.Close();
        
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static void DrawDownArrow(SKCanvas canvas, float x, float y, float size, bool isLightTheme)
    {
        using var paint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(87, 111, 118) : new SKColor(215, 218, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        
        float w = size * 0.8f;
        float h = size * 0.9f;

        using var builder = new SKPathBuilder();
        builder.MoveTo(new SKPoint(x, y + h / 2));
        builder.LineTo(new SKPoint(x - w / 2, y + h / 2 - w / 2));
        builder.LineTo(new SKPoint(x - w / 4, y + h / 2 - w / 2));
        builder.LineTo(new SKPoint(x - w / 4, y - h / 2));
        builder.LineTo(new SKPoint(x + w / 4, y - h / 2));
        builder.LineTo(new SKPoint(x + w / 4, y + h / 2 - w / 2));
        builder.LineTo(new SKPoint(x + w / 2, y + h / 2 - w / 2));
        builder.Close();
        
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static void DrawCommentIcon(SKCanvas canvas, float x, float y, float size, bool isLightTheme)
    {
        using var paint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(87, 111, 118) : new SKColor(215, 218, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        
        float w = size * 0.55f; 
        float h = size * 0.45f; 
        float r = size * 0.25f; 

        using var builder = new SKPathBuilder();
        builder.ArcTo(new SKRect(x - w, y - h, x - w + 2 * r, y - h + 2 * r), 180, 90, false);
        builder.ArcTo(new SKRect(x + w - 2 * r, y - h, x + w, y - h + 2 * r), 270, 90, false);
        builder.ArcTo(new SKRect(x + w - 2 * r, y + h - 2 * r, x + w, y + h), 0, 90, false);
        builder.LineTo(new SKPoint(x - w + 2 * r, y + h));
        builder.LineTo(new SKPoint(x - w - size * 0.15f, y + h + size * 0.15f));
        builder.LineTo(new SKPoint(x - w, y + h - r));
        builder.Close();
        
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private SKTypeface GetEmojiTypeface()
    {
        string[] emojiFonts = { "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji" };
        foreach (var family in emojiFonts)
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(family);
                if (tf != null && !tf.FamilyName.Equals("Portable User Interface", StringComparison.OrdinalIgnoreCase))
                {
                    return tf;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load emoji font '{FontFamily}'", family);
            }
        }
        return SKTypeface.Default;
    }

    private static bool IsEmoji(string s, int index, out int charCount)
    {
        if (index >= s.Length)
        {
            charCount = 0;
            return false;
        }

        int codePoint = char.ConvertToUtf32(s, index);
        charCount = char.IsSurrogatePair(s, index) ? 2 : 1;

        if ((codePoint >= 0x1F600 && codePoint <= 0x1F64F) || // Emoticons
            (codePoint >= 0x1F300 && codePoint <= 0x1F5FF) || // Misc Symbols/Pictographs
            (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) || // Transport/Map
            (codePoint >= 0x1F900 && codePoint <= 0x1F9FF) || // Supplemental Pictographs
            (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF) || // Pictographs Extended-A
            (codePoint >= 0x2600 && codePoint <= 0x27BF) ||  // Misc Symbols & Dingbats
            (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF))   // Flags
        {
            return true;
        }

        return false;
    }

    private static float MeasureTextWithEmoji(string text, SKFont textFont, SKFont emojiFont)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        float width = 0;
        int i = 0;
        while (i < text.Length)
        {
            bool currentIsEmoji = IsEmoji(text, i, out int len);
            int start = i;
            i += len;
            while (i < text.Length && IsEmoji(text, i, out int nextLen) == currentIsEmoji)
            {
                i += nextLen;
            }

            var runText = text.Substring(start, i - start);
            var font = currentIsEmoji ? emojiFont : textFont;
            width += font.MeasureText(runText);
        }
        return width;
    }

    private static void DrawTextWithEmoji(SKCanvas canvas, string text, float x, float y, SKTextAlign align, SKFont textFont, SKFont emojiFont, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return;

        var runs = new List<(string Content, bool IsEmoji)>();
        int i = 0;
        while (i < text.Length)
        {
            bool currentIsEmoji = IsEmoji(text, i, out int len);
            int start = i;
            i += len;
            while (i < text.Length && IsEmoji(text, i, out int nextLen) == currentIsEmoji)
            {
                i += nextLen;
            }

            runs.Add((text.Substring(start, i - start), currentIsEmoji));
        }

        float totalWidth = 0;
        foreach (var run in runs)
        {
            var font = run.IsEmoji ? emojiFont : textFont;
            totalWidth += font.MeasureText(run.Content);
        }

        float currentX = x;
        if (align == SKTextAlign.Center)
        {
            currentX = x - totalWidth / 2;
        }
        else if (align == SKTextAlign.Right)
        {
            currentX = x - totalWidth;
        }

        foreach (var run in runs)
        {
            var font = run.IsEmoji ? emojiFont : textFont;
            canvas.DrawText(run.Content, currentX, y, SKTextAlign.Left, font, paint);
            currentX += font.MeasureText(run.Content);
        }
    }

    public async Task<byte[]> GenerateTitleCard(
        string title,
        string subreddit,
        string subredditIconPath,
        bool isLightTheme = false,
        string votes = "4.1K",
        string comments = "2.2K",
        string timeAgo = "2d ago",
        string username = "")
    {
        int w = 1080, h = 1920;
        float cardW = 920;
        float cardX = (w - cardW) / 2;
        float cornerRadius = 24;
        float padding = 32;
        float headerTitleGap = 18;
        float titleFooterGap = 24;
        float footerHeight = 54;

        // Wrapped title text lines
        var titleTypeface = GetTypeface(bold: true);
        var titleFont = new SKFont(titleTypeface, 38);
        var emojiTypeface = GetEmojiTypeface();
        var emojiFont = new SKFont(emojiTypeface, 38);
        var wrappedLines = WrapText(title, (int)(cardW - padding * 2), titleFont, emojiFont);
        if (wrappedLines.Count == 0) wrappedLines.Add("");

        float titleLineHeight = 52;
        float titleHeight = wrappedLines.Count * titleLineHeight;

        // Calculate card height dynamically (tight wrap)
        float headerHeight = 58;
        float totalContentHeight = padding + headerHeight + headerTitleGap + titleHeight + titleFooterGap + footerHeight + padding;
        float cardH = totalContentHeight;
        float cardY = (h - cardH) / 2;

        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Background of the card
        var bgPaint = new SKPaint
        {
            Color = isLightTheme ? SKColors.White : new SKColor(15, 20, 22),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, cornerRadius, cornerRadius, bgPaint);

        if (isLightTheme)
        {
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(220, 224, 230),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f
            };
            canvas.DrawRoundRect(cardX, cardY, cardW, cardH, cornerRadius, cornerRadius, borderPaint);
        }

        float topY = cardY + padding;
        float leftX = cardX + padding;
        float rightX = cardX + cardW - padding;

        // 1. Subreddit Icon
        float iconSize = 50;
        var iconRect = new SKRect(leftX, topY, leftX + iconSize, topY + iconSize);
        canvas.Save();
        using var clipBuilder = new SKPathBuilder();
        clipBuilder.AddCircle(leftX + iconSize / 2, topY + iconSize / 2, iconSize / 2, SKPathDirection.Clockwise);
        using var clipPath = clipBuilder.Detach();
        canvas.ClipPath(clipPath, SkiaSharp.SKClipOperation.Intersect, true);

        SKBitmap? iconBitmap = null;
        if (!string.IsNullOrWhiteSpace(subredditIconPath))
        {
            try
            {
                if (subredditIconPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    subredditIconPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    using var client = clientFactory.CreateClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                    var bytes = await client.GetByteArrayAsync(subredditIconPath);
                    iconBitmap = SKBitmap.Decode(bytes);
                }
                else if (File.Exists(subredditIconPath))
                {
                    iconBitmap = SKBitmap.Decode(subredditIconPath);
                }
            }
            catch
            {
                // Fallback
            }
        }

        if (iconBitmap != null)
        {
            canvas.DrawBitmap(iconBitmap, iconRect, new SKSamplingOptions(SKFilterMode.Linear), new SKPaint { IsAntialias = true });
            iconBitmap.Dispose();
        }
        else
        {
            // Draw default reddit logo-like orange-red circle
            using var defaultBgPaint = new SKPaint
            {
                Color = new SKColor(255, 69, 0),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(leftX + iconSize / 2, topY + iconSize / 2, iconSize / 2, defaultBgPaint);

            using var defaultTextPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            var defaultFont = new SKFont(GetTypeface(bold: true), 28);
            canvas.DrawText("r", leftX + iconSize / 2, topY + 34, SKTextAlign.Center, defaultFont, defaultTextPaint);
        }
        canvas.Restore();

        // 2. Subreddit Name, Username, and Date
        float textX = leftX + iconSize + 15;

        var subTypefaceBold = GetTypeface(bold: true);
        var subTypefaceNormal = GetTypeface(bold: false);

        var subFont = new SKFont(subTypefaceBold, 26);
        using var subPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(26, 26, 27) : SKColors.White,
            IsAntialias = true
        };
        canvas.DrawText(subreddit, textX, topY + 20, SKTextAlign.Left, subFont, subPaint);

        var metaFont = new SKFont(subTypefaceNormal, 22);
        using var metaPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(87, 111, 118) : new SKColor(135, 149, 161),
            IsAntialias = true
        };

        string line2Text = "";
        if (!string.IsNullOrWhiteSpace(username))
        {
            string cleanUsername = username.StartsWith("u/", StringComparison.OrdinalIgnoreCase) ? username : $"u/{username}";
            line2Text = $"{cleanUsername} • {timeAgo}";
        }
        else
        {
            line2Text = timeAgo;
        }

        canvas.DrawText(line2Text, textX, topY + 48, SKTextAlign.Left, metaFont, metaPaint);

        // 3. Ellipsis
        var ellipsisFont = new SKFont(subTypefaceBold, 32);
        using var ellipsisPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(87, 111, 118) : new SKColor(135, 149, 161),
            IsAntialias = true
        };
        canvas.DrawText("...", rightX - 15, topY + 31, SKTextAlign.Center, ellipsisFont, ellipsisPaint);

        // 4. Title Text
        float titleTopY = topY + iconSize + headerTitleGap;
        using var titleTextPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(26, 26, 27) : SKColors.White,
            IsAntialias = true
        };

        float currentY = titleTopY + 36;
        foreach (var line in wrappedLines)
        {
            DrawTextWithEmoji(canvas, line, leftX, currentY, SKTextAlign.Left, titleFont, emojiFont, titleTextPaint);
            currentY += titleLineHeight;
        }

        // 5. Footer Buttons
        float footerTopY = cardY + cardH - padding - footerHeight;
        float pillHeight = footerHeight;
        float pillCornerRadius = pillHeight / 2;

        using var pillBgPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(237, 239, 241) : new SKColor(30, 43, 48),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var footerFont = new SKFont(subTypefaceBold, 24);
        using var footerTextPaint = new SKPaint
        {
            Color = isLightTheme ? new SKColor(26, 26, 27) : SKColors.White,
            IsAntialias = true
        };

        // Draw Up/Down vote pill
        float voteTextWidth = footerFont.MeasureText(votes);
        float votePillPaddingX = 24;
        float arrowSize = 20;
        float arrowTextGap = 10;
        float votePillWidth = votePillPaddingX + arrowSize + arrowTextGap + voteTextWidth + arrowTextGap + arrowSize + votePillPaddingX;
        var votePillRect = new SKRect(leftX, footerTopY, leftX + votePillWidth, footerTopY + pillHeight);
        canvas.DrawRoundRect(votePillRect, pillCornerRadius, pillCornerRadius, pillBgPaint);

        float upArrowX = leftX + votePillPaddingX + arrowSize / 2;
        float pillCenterY = footerTopY + pillHeight / 2;
        DrawUpArrow(canvas, upArrowX, pillCenterY, arrowSize, isLightTheme);

        float voteTextX = leftX + votePillPaddingX + arrowSize + arrowTextGap;
        canvas.DrawText(votes, voteTextX, pillCenterY + 8, SKTextAlign.Left, footerFont, footerTextPaint);

        float downArrowX = voteTextX + voteTextWidth + arrowTextGap + arrowSize / 2;
        DrawDownArrow(canvas, downArrowX, pillCenterY, arrowSize, isLightTheme);

        // Draw Comments pill
        float commentTextWidth = footerFont.MeasureText(comments);
        float commentPillPaddingX = 24;
        float commentPillWidth = commentPillPaddingX + arrowSize + arrowTextGap + commentTextWidth + commentPillPaddingX;
        float commentPillX = leftX + votePillWidth + 16;
        var commentPillRect = new SKRect(commentPillX, footerTopY, commentPillX + commentPillWidth, footerTopY + pillHeight);
        canvas.DrawRoundRect(commentPillRect, pillCornerRadius, pillCornerRadius, pillBgPaint);

        float commentIconX = commentPillX + commentPillPaddingX + arrowSize / 2;
        DrawCommentIcon(canvas, commentIconX, pillCenterY, arrowSize, isLightTheme);

        float commentTextX = commentPillX + commentPillPaddingX + arrowSize + arrowTextGap;
        canvas.DrawText(comments, commentTextX, pillCenterY + 8, SKTextAlign.Left, footerFont, footerTextPaint);

        // Save image to disk
        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        
        try
        {
            await File.WriteAllBytesAsync("/Volumes/realme/Dev/content-farm/reddit/ballers.png", data.ToArray());
        }
        catch
        {
            // Fallback: write to current directory if parent path is not writable/present
            await File.WriteAllBytesAsync("ballers.png", data.ToArray());
        }

        return data.ToArray();
    }

    private static List<string> WrapText(string text, int maxWidth, SKFont font, SKFont emojiFont)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = new List<string>();

        foreach (var word in words)
        {
            var testLine = currentLine.Count == 0
                ? word
                : string.Join(" ", currentLine.Append(word));

            float width = MeasureTextWithEmoji(testLine, font, emojiFont);

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
