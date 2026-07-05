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

    /// <summary>
    /// Creates or updates a file in <paramref name="repo"/> (owner/name) at <paramref name="path"/>
    /// on <paramref name="branch"/>. Fetches the existing file SHA first when updating.
    /// </summary>
    Task UpsertFileAsync(string repo, string path, string content, string branch, string commitMessage, CancellationToken ct);

    /// <summary>
    /// Returns the UTF-8 text content of the file at <paramref name="path"/> in <paramref name="repo"/>
    /// on <paramref name="branch"/>, or <c>null</c> if the file does not exist.
    /// </summary>
    Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct);
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
            var blobContent = blobDoc.RootElement.GetProperty("content").GetString()!;

            // PUT file into target repo (GitHub Contents API accepts base64 content directly).
            var putUrl = $"/repos/{Uri.EscapeDataString(targetOwner)}/{Uri.EscapeDataString(targetName)}/contents/{Uri.EscapeDataString(destPath)}";
            var putBody = JsonSerializer.Serialize(new PutFileRequest(
                Message: $"chore: seed scaffold ({destPath})",
                Content: blobContent.Replace("\n", "")));  // strip line-breaks GitHub adds to base64

            using var putResp = await client.PutAsync(putUrl,
                new StringContent(putBody, Encoding.UTF8, "application/json"), ct);

            if (!putResp.IsSuccessStatusCode)
                throw new GitHubApiException(
                    $"Failed to write scaffold file '{destPath}' to '{targetRepo}': {(int)putResp.StatusCode}");
        }
    }

    public async Task UpsertFileAsync(string repo, string path, string content, string branch, string commitMessage, CancellationToken ct)
    {
        var opts = options.Value;
        var client = BuildClient();

        // Split "owner/name" — fall back to configured org if no slash present.
        var slash = repo.IndexOf('/');
        var owner = slash >= 0 ? repo[..slash] : opts.Owner;
        var name  = slash >= 0 ? repo[(slash + 1)..] : repo;

        var encodedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var getUrl = $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/contents/{encodedPath}";

        // Fetch existing SHA (null if file does not exist yet).
        string? sha = null;
        using (var getResp = await client.GetAsync(getUrl + $"?ref={Uri.EscapeDataString(branch)}", ct))
        {
            if (getResp.IsSuccessStatusCode)
            {
                var getJson = await getResp.Content.ReadAsStringAsync(ct);
                using var getDoc = JsonDocument.Parse(getJson);
                if (getDoc.RootElement.TryGetProperty("sha", out var shaEl))
                    sha = shaEl.GetString();
            }
            // 404 = new file; anything else we'll try the PUT and surface the error there.
        }

        var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var body = JsonSerializer.Serialize(new UpsertFileRequest(
            Message: commitMessage,
            Content: base64Content,
            Branch: branch,
            Sha: sha));

        using var putContent = new StringContent(body, Encoding.UTF8, "application/json");
        using var putResp = await client.PutAsync(getUrl, putContent, ct);

        if (!putResp.IsSuccessStatusCode)
        {
            var errorBody = await putResp.Content.ReadAsStringAsync(ct);
            throw new GitHubApiException(
                $"GitHub API returned {(int)putResp.StatusCode} upserting '{path}' in '{repo}': {errorBody}");
        }
    }

    public async Task<string?> GetFileContentAsync(string repo, string path, string branch, CancellationToken ct)
    {
        var opts = options.Value;
        var client = BuildClient();

        var slash = repo.IndexOf('/');
        var owner = slash >= 0 ? repo[..slash] : opts.Owner;
        var name  = slash >= 0 ? repo[(slash + 1)..] : repo;

        var encodedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var url = $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/contents/{encodedPath}?ref={Uri.EscapeDataString(branch)}";

        using var resp = await client.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new GitHubApiException(
                $"GitHub API returned {(int)resp.StatusCode} reading '{path}' from '{repo}': {errorBody}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var contentEl))
            return null;

        var base64 = contentEl.GetString()?.Replace("\n", "").Replace("\r", "");
        if (string.IsNullOrEmpty(base64)) return null;
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
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

    private sealed record UpsertFileRequest(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("content")]  string Content,
        [property: JsonPropertyName("branch")]   string Branch,
        [property: JsonPropertyName("sha")]      string? Sha);
}
