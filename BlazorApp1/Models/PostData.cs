namespace BlazorApp1.Models;

public record PostData(string Title, string Body, string Author, string OldUrl, string Votes, string Comments, string TimeAgo);

public class RedditException(string message, Exception? inner = null)
    : Exception(message, inner);

public class RedditPostNotFoundException(string url, string reason)
    : RedditException($"Could not find post content at {url}: {reason}");

public class RedditFetchException(string url, string reason, Exception inner)
    : RedditException($"Failed to fetch {url}: {reason}", inner);
