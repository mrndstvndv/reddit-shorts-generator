using RedditShortMaker.Services;
using RedditShortMaker.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace RedditShortMaker.IntegrationTests;

public class RedditScraperTests
{
    private static RedditService CreateService()
    {
        var factory = new TestHttpClientFactory();
        return new RedditService(factory, NullLogger<RedditService>.Instance);
    }

    [Fact]
    public async Task GetPost_ValidUrl_ReturnsCompletePostData()
    {
        var service = CreateService();
        var url = "https://www.reddit.com/r/buhaydigital/comments/1uf64ez/wfh_things_that_make_me_feel_rich_for_no_reason/";

        var post = await service.GetPost(url);

        Assert.NotNull(post);
        Assert.False(string.IsNullOrWhiteSpace(post.Title), "Title should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(post.Body), "Body should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(post.Author), "Author should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(post.Votes), "Votes should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(post.Comments), "Comments should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(post.TimeAgo), "TimeAgo should not be empty");
        
        // Assert they are not dummy values if it's the real post
        Assert.NotEqual("username", post.Author);
    }

    [Fact]
    public async Task GetPost_InvalidUrl_ThrowsRedditFetchException()
    {
        var service = CreateService();
        // A URL that is guaranteed to fail connection (port 9999 on localhost)
        var url = "http://localhost:9999/r/test";

        await Assert.ThrowsAsync<RedditFetchException>(() => service.GetPost(url));
    }

    [Fact]
    public async Task GetPost_InvalidDomain_ThrowsRedditPostNotFoundException()
    {
        var service = CreateService();
        // Will rewrite to old.reddit.com/r/test and fail because it's not a post page
        var url = "https://invalid-domain-name-that-is-not-reddit.com/r/test";

        await Assert.ThrowsAsync<RedditPostNotFoundException>(() => service.GetPost(url));
    }

    [Fact]
    public async Task GetPost_NonExistentPost_ThrowsRedditPostNotFoundException()
    {
        var service = CreateService();
        // A valid old.reddit URL format but a comment path that doesn't exist
        var url = "https://old.reddit.com/r/buhaydigital/comments/nonexistentpost12345/";

        await Assert.ThrowsAsync<RedditPostNotFoundException>(() => service.GetPost(url));
    }
}

file class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name = "") => new();
}
