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

    /// <summary>
    /// Copies the contents of the "scaffold/" folder from the configured ScaffoldRepo into
    /// the given repo (owner/name). No-op when ScaffoldRepo is not configured.
    /// </summary>
    Task SeedScaffoldAsync(string targetRepo, CancellationToken ct);
}

public sealed class GitHubService(
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubOptions> options) : IGitHubService
{
    public const string HttpClientName = "github";

    public async Task<string> CreateRepoAsync(string repoName, CancellationToken ct)
    {
        var opts = options.Value;
        var client = BuildClient();

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

    public async Task SeedScaffoldAsync(string targetRepo, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ScaffoldRepo)) return;

        var client = BuildClient();

        // Resolve scaffold source owner/repo.
        var parts = opts.ScaffoldRepo.Split('/', 2);
        var scaffoldOwner = parts.Length == 2 ? parts[0] : opts.Owner;
        var scaffoldRepo  = parts.Length == 2 ? parts[1] : parts[0];

        // 1. Get the full recursive tree of the scaffold source repo.
        var treeUrl = $"/repos/{Uri.EscapeDataString(scaffoldOwner)}/{Uri.EscapeDataString(scaffoldRepo)}/git/trees/main?recursive=1";
        using var treeResp = await client.GetAsync(treeUrl, ct);
        if (!treeResp.IsSuccessStatusCode)
            throw new GitHubApiException($"Could not read scaffold tree from '{opts.ScaffoldRepo}': {(int)treeResp.StatusCode}");

        using var treeDoc = JsonDocument.Parse(await treeResp.Content.ReadAsStringAsync(ct));
        var blobs = treeDoc.RootElement.GetProperty("tree").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == "blob"
                     && n.GetProperty("path").GetString()!.StartsWith("scaffold/", StringComparison.Ordinal))
            .Select(n => (
                path: n.GetProperty("path").GetString()!,
                sha:  n.GetProperty("sha").GetString()!))
            .ToList();

        // 2. Resolve target repo owner/name.
        var targetParts = targetRepo.Split('/', 2);
        var targetOwner = targetParts.Length == 2 ? targetParts[0] : opts.Owner;
        var targetName  = targetParts.Length == 2 ? targetParts[1] : targetParts[0];

        // 3. Copy each blob into the target repo, stripping the leading "scaffold/" prefix.
        foreach (var (path, sha) in blobs)
        {
            var destPath = path["scaffold/".Length..];

            // Fetch blob content (base64) from scaffold source.
            var blobUrl = $"/repos/{Uri.EscapeDataString(scaffoldOwner)}/{Uri.EscapeDataString(scaffoldRepo)}/git/blobs/{sha}";
            using var blobResp = await client.GetAsync(blobUrl, ct);
            if (!blobResp.IsSuccessStatusCode) continue; // skip unreadable files

            using var blobDoc = JsonDocument.Parse(await blobResp.Content.ReadAsStringAsync(ct));
            var content = blobDoc.RootElement.GetProperty("content").GetString()!;

            // PUT file into target repo (GitHub Contents API accepts base64 content directly).
            var putUrl = $"/repos/{Uri.EscapeDataString(targetOwner)}/{Uri.EscapeDataString(targetName)}/contents/{Uri.EscapeDataString(destPath)}";
            var putBody = JsonSerializer.Serialize(new PutFileRequest(
                Message: $"chore: seed scaffold ({destPath})",
                Content: content.Replace("\n", "")));  // strip line-breaks GitHub adds to base64

            using var putResp = await client.PutAsync(putUrl,
                new StringContent(putBody, System.Text.Encoding.UTF8, "application/json"), ct);

            if (!putResp.IsSuccessStatusCode)
                throw new GitHubApiException(
                    $"Failed to write scaffold file '{destPath}' to '{targetRepo}': {(int)putResp.StatusCode}");
        }
    }

    private HttpClient BuildClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Value.Pat);
        return client;
    }

    private sealed record CreateRepoRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("private")] bool Private);

    private sealed record PutFileRequest(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("content")]  string Content);
}
