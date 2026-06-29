using HtmlAgilityPack;
using RedditShortMaker.Models;
using System.Diagnostics;

namespace RedditShortMaker.Services;

public class RedditService(IHttpClientFactory clientFactory, ILogger<RedditService> logger)
{
    private static readonly string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public async Task<PostData> GetPost(string url)
    {
        var resolved = await ResolveUrlWithCurl(url);

        string html;
        try
        {
            html = await FetchHtmlWithCurl(resolved);
        }
        catch (Exception ex)
        {
            throw new RedditFetchException(url, ex.Message, ex);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var postNode = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class, 'thing') and contains(@data-type, 'link')]");

        if (postNode is null)
            throw new RedditPostNotFoundException(url, "post container (thing) not found");

        var rawTitle = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class, 'title') and contains(@class, 'may-blank')]")?.InnerText.Trim();

        if (rawTitle is null)
            throw new RedditPostNotFoundException(url, "title not found");

        var title = System.Net.WebUtility.HtmlDecode(rawTitle);

        var bodyNode = postNode.SelectSingleNode(".//div[contains(@class, 'usertext-body')]//div[contains(@class, 'md')]");
        if (bodyNode is null)
            throw new RedditPostNotFoundException(url, "body node not found");

        var rawBody = bodyNode.InnerText.Trim();
        if (string.IsNullOrEmpty(rawBody))
            throw new RedditPostNotFoundException(url, "body text is empty");

        var body = System.Net.WebUtility.HtmlDecode(rawBody);

        var author = postNode.GetAttributeValue("data-author", string.Empty);
        if (string.IsNullOrEmpty(author))
            throw new RedditPostNotFoundException(url, "author not found");

        var commentsCount = postNode.GetAttributeValue("data-comments-count", -1);
        if (commentsCount < 0)
            throw new RedditPostNotFoundException(url, "comments count attribute not found");

        var score = postNode.GetAttributeValue("data-score", -1);
        if (score < 0)
            throw new RedditPostNotFoundException(url, "score/votes attribute not found");

        var timeNode = postNode.SelectSingleNode(".//time[contains(@class, 'live-timestamp')]");
        if (timeNode is null)
            throw new RedditPostNotFoundException(url, "time ago (live-timestamp) not found");

        var timeAgoText = timeNode.InnerText.Trim();
        if (string.IsNullOrEmpty(timeAgoText))
            throw new RedditPostNotFoundException(url, "time ago text is empty");

        var formattedVotes = FormatRedditNumber(score);
        var formattedComments = FormatRedditNumber(commentsCount);

        return new PostData(
            Title: title,
            Body: body,
            Author: author,
            OldUrl: resolved,
            Votes: formattedVotes,
            Comments: formattedComments,
            TimeAgo: timeAgoText
        );
    }

    private static string FormatRedditNumber(int num)
    {
        if (num >= 1_000_000)
            return (num / 1_000_000.0).ToString("0.#") + "M";
        if (num >= 1_000)
            return (num / 1_000.0).ToString("0.#") + "K";
        return num.ToString();
    }

    private static async Task<string> ResolveUrlWithCurl(string url)
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

        var targetUrl = url;
        if (uri.AbsolutePath.Contains("/s/"))
        {
            var resolveUri = new UriBuilder(uri) { Host = "www.reddit.com" }.Uri.ToString();
            
            var psi = new ProcessStartInfo("curl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("-L");
            psi.ArgumentList.Add("-I");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("/dev/null");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(UserAgent);
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("%{url_effective}");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("10");
            psi.ArgumentList.Add(resolveUri);

            using var proc = new Process { StartInfo = psi };
            try
            {
                proc.Start();
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                
                if (proc.ExitCode == 0)
                {
                    var effectiveUrl = (await outputTask).Trim();
                    if (!string.IsNullOrEmpty(effectiveUrl))
                    {
                        targetUrl = effectiveUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RedditFetchException(url, $"curl redirect resolution failed: {ex.Message}", ex);
            }
        }

        try
        {
            var targetUri = new Uri(targetUrl);
            return new UriBuilder(targetUri) { Host = "old.reddit.com" }.Uri.ToString();
        }
        catch (Exception ex)
        {
            throw new RedditFetchException(url, $"failed to resolve redirect: {ex.Message}", ex);
        }
    }

    private static async Task<string> FetchHtmlWithCurl(string url)
    {
        var psi = new ProcessStartInfo("curl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add("-A");
        psi.ArgumentList.Add(UserAgent);
        psi.ArgumentList.Add("--max-time");
        psi.ArgumentList.Add("15");
        psi.ArgumentList.Add(url);

        using var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = await stderrTask;
                throw new RedditException($"curl exited with code {proc.ExitCode}: {err}");
            }

            return await stdoutTask;
        }
        catch (Exception ex) when (ex is not RedditException)
        {
            throw new RedditException($"failed to execute curl: {ex.Message}", ex);
        }
    }
}
