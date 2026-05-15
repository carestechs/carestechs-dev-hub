using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Identity.Tests;

[Collection("postgres")]
public class AuthEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _client = null!;
    private const string SeedEmail = "op@test.local";
    private const string SeedPassword = "OperatorTest123!";

    public AuthEndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"auth_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            OperatorEmail = SeedEmail,
            OperatorPassword = SeedPassword,
        };
        // Boot the WebApplicationFactory (this also triggers the seeders).
        _client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        // Wait for seeders by hitting /health (DB ready means migrations + seeds ran).
        var resp = await _client.GetAsync("/health");
        resp.IsSuccessStatusCode.Should().BeTrue();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_with_seed_operator_returns_jwt_and_sets_refresh_cookie()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email = SeedEmail, password = SeedPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("data").GetProperty("member").GetProperty("email").GetString().Should().Be(SeedEmail);

        resp.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("refresh="));
    }

    [Fact]
    public async Task Login_with_bad_password_returns_401_problem_json()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email = SeedEmail, password = "WRONG" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().Be("/probs/unauthorized");
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var resp = await _client.GetAsync("/api/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_with_login_token_returns_seed_member_and_empty_memberships()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = SeedEmail, password = SeedPassword });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Authorization = new("Bearer", token);
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await resp.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("data").GetProperty("member").GetProperty("email").GetString().Should().Be(SeedEmail);
        me.GetProperty("data").GetProperty("memberships").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_returns_new_access_token()
    {
        // Initial login to seed the refresh cookie.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = SeedEmail, password = SeedPassword });
        login.Headers.TryGetValues("Set-Cookie", out var firstCookies).Should().BeTrue();
        var firstRefresh = firstCookies!.First(c => c.StartsWith("refresh="));
        var firstRefreshValue = firstRefresh.Split(';')[0]; // refresh=...

        // Use the refresh cookie to rotate.
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", firstRefreshValue);
        var refreshResp = await _client.SendAsync(refreshReq);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);

        refreshResp.Headers.TryGetValues("Set-Cookie", out var rotatedCookies).Should().BeTrue();
        var newRefresh = rotatedCookies!.First(c => c.StartsWith("refresh="));
        newRefresh.Split(';')[0].Should().NotBe(firstRefreshValue, "rotation must produce a fresh token");

        // The reissued access token must be present.
        var body = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_without_cookie_returns_401()
    {
        var resp = await _client.PostAsync("/api/auth/refresh", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
