using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevHub.Modules.Workspace.Exceptions;
using DevHub.Modules.Workspace.Options;
using Microsoft.Extensions.Options;

namespace DevHub.Modules.Workspace.Services;

public interface IGitHubService
{
    /// <summary>Creates a private repo under the configured org and returns its full_name (owner/name).</summary>
    Task<string> CreateRepoAsync(string repoName, CancellationToken ct);
}

public sealed class GitHubService(
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubOptions> options) : IGitHubService
{
    public const string HttpClientName = "github";

    public async Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.Pat);

        var body = JsonSerializer.Serialize(new CreateRepoRequest(repoName, Private: true));
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var url = $"/orgs/{Uri.EscapeDataString(opts.Owner)}/repos";
        using var response = await client.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new GitHubApiException(
                $"GitHub API returned {(int)response.StatusCode} creating repo '{opts.Owner}/{repoName}': {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("full_name", out var fullName))
            throw new GitHubApiException("GitHub API response did not include full_name.");

        return fullName.GetString()!;
    }

    private sealed record CreateRepoRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("private")] bool Private);
}
