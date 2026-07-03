using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevHub.Modules.Workspace.Exceptions;
using DevHub.Modules.Workspace.Options;
using DevHub.Modules.Workspace.Services;
using FluentAssertions;
using MsOptions = Microsoft.Extensions.Options;

namespace DevHub.Modules.Workspace.Tests;

public class GitHubServiceTests
{
    private static IGitHubService BuildService(
        HttpStatusCode statusCode,
        string responseBody,
        string pat = "test-pat",
        string owner = "test-org")
    {
        var handler = new FakeHttpMessageHandler(statusCode, responseBody);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var factory = new SingleClientFactory(GitHubService.HttpClientName, client);
        var options = MsOptions.Options.Create(new GitHubOptions { Pat = pat, Owner = owner });
        return new GitHubService(factory, options);
    }

    [Fact]
    public async Task CreateRepoAsync_Returns_FullName_On_201()
    {
        var service = BuildService(
            HttpStatusCode.Created,
            """{"id":1,"full_name":"test-org/my-repo","private":true}""");

        var result = await service.CreateRepoAsync("my-repo", CancellationToken.None);

        result.Should().Be("test-org/my-repo");
    }

    [Fact]
    public async Task CreateRepoAsync_Throws_GitHubApiException_On_Non2xx()
    {
        var service = BuildService(
            HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed"}""");

        var act = () => service.CreateRepoAsync("bad-repo", CancellationToken.None);

        await act.Should().ThrowAsync<GitHubApiException>()
            .WithMessage("*422*");
    }

    [Fact]
    public async Task CreateRepoAsync_Throws_GitHubApiException_When_FullName_Missing()
    {
        var service = BuildService(
            HttpStatusCode.Created,
            """{"id":1,"private":true}""");

        var act = () => service.CreateRepoAsync("no-name-repo", CancellationToken.None);

        await act.Should().ThrowAsync<GitHubApiException>()
            .WithMessage("*full_name*");
    }

    [Fact]
    public async Task CreateRepoAsync_Sets_Authorization_Header()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created,
            """{"id":1,"full_name":"org/repo","private":true}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var factory = new SingleClientFactory(GitHubService.HttpClientName, client);
        var options = MsOptions.Options.Create(new GitHubOptions { Pat = "my-secret-pat", Owner = "org" });
        var service = new GitHubService(factory, options);

        await service.CreateRepoAsync("repo", CancellationToken.None);

        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("my-secret-pat");
    }

    [Fact]
    public async Task CreateRepoAsync_Posts_To_Org_Repos_Endpoint()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.Created,
            """{"id":1,"full_name":"carestechs/my-project","private":true}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        var factory = new SingleClientFactory(GitHubService.HttpClientName, client);
        var options = MsOptions.Options.Create(new GitHubOptions { Pat = "p", Owner = "carestechs" });
        var service = new GitHubService(factory, options);

        await service.CreateRepoAsync("my-project", CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/orgs/carestechs/repos");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private sealed class FakeHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHttpMessageHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SingleClientFactory(string expectedName, HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            if (name != expectedName)
                throw new ArgumentException($"Expected client name '{expectedName}', got '{name}'.");
            return client;
        }
    }
}
