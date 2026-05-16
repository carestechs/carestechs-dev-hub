using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevHub.Contracts.Audit;
using DevHub.Modules.Workspace.Tests.Helpers;
using DevHub.TestHarness;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

[Collection("postgres")]
public class EndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DevHubApiFactory _factory = null!;
    private HttpClient _operator = null!;

    public EndpointsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var connStr = await _pg.CreateIsolatedDatabaseAsync($"ws_{Guid.NewGuid():N}");
        _factory = new DevHubApiFactory
        {
            ConnectionString = connStr,
            OperatorEmail = "op@test.local",
            OperatorPassword = "OperatorTest123!",
        };
        // Force the host to spin up so the seeders run.
        (await _factory.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();
        _operator = await _factory.LoginOperatorAsync();
    }

    public Task DisposeAsync()
    {
        _operator.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ----- Teams -----------------------------------------------------------

    [Fact]
    public async Task Teams_create_as_operator_returns_200_with_audit()
    {
        var resp = await _operator.PostAsJsonAsync("/api/teams", new { name = "Engineering" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // DB is shared across the class's tests; assert that at least one Granted
        // team:create row exists.
        var entries = await _factory.AuditEntriesForActionAsync("team:create");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted);
    }

    [Fact]
    public async Task Teams_create_as_non_operator_returns_403_with_audit()
    {
        var (client, _) = await _factory.LoginFreshMemberAsync("alice@test.local", "Pw_Alice_123!", "Alice");
        var resp = await client.PostAsJsonAsync("/api/teams", new { name = "ShouldFail" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var entries = await _factory.AuditEntriesForActionAsync("team:create");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task Teams_create_anonymous_returns_401()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/teams", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Teams_delete_with_owned_projects_returns_409()
    {
        var team = await CreateTeamAsync("Owners");
        await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = "Project With Owner",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = team.Id,
        });

        var resp = await _operator.DeleteAsync($"/api/teams/{team.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Teams_list_returns_paginated_envelope()
    {
        await CreateTeamAsync("Alpha");
        await CreateTeamAsync("Beta");

        var resp = await _operator.GetAsync("/api/teams?page=1&pageSize=10");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("meta").GetProperty("page").GetInt32().Should().Be(1);
    }

    // ----- Members ---------------------------------------------------------

    [Fact]
    public async Task Members_invite_as_operator_returns_200_with_audit()
    {
        var resp = await _operator.PostAsJsonAsync("/api/members", new
        {
            displayName = "Bob",
            email = $"bob-{Guid.NewGuid():N}@test.local",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await _factory.AuditEntriesForActionAsync("member:invite");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted);
    }

    [Fact]
    public async Task Members_invite_as_non_operator_returns_403()
    {
        var (client, _) = await _factory.LoginFreshMemberAsync("c1@test.local", "Pw_C_123!", "Carl");
        var resp = await client.PostAsJsonAsync("/api/members", new
        {
            displayName = "Bob",
            email = "bob2@test.local",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Members_invite_duplicate_email_returns_409()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        var first = await _operator.PostAsJsonAsync("/api/members", new { displayName = "First", email });
        first.EnsureSuccessStatusCode();
        var second = await _operator.PostAsJsonAsync("/api/members", new { displayName = "Second", email });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ----- Projects --------------------------------------------------------

    [Fact]
    public async Task Projects_create_as_operator_returns_200_with_audit()
    {
        var team = await CreateTeamAsync("ProjOwners");
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Proj-{Guid.NewGuid():N}",
            slug = $"p-{Guid.NewGuid():N}".Substring(0, 14),
            projectType = "feature-delivery",
            owningTeamId = team.Id,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _factory.AuditEntriesForActionAsync("project:create");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted);
    }

    [Fact]
    public async Task Projects_create_duplicate_slug_returns_409()
    {
        var team = await CreateTeamAsync("DupOwners");
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var first = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Proj-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = team.Id,
        });
        first.EnsureSuccessStatusCode();

        var second = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Proj-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = team.Id,
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Projects_read_as_non_member_returns_403_with_audit()
    {
        var (alice, _) = await _factory.LoginFreshMemberAsync("alice2@test.local", "Pw_A_123!", "Alice2");

        var team = await CreateTeamAsync("Secret");
        var project = await CreateProjectAsync(team.Id);

        var resp = await alice.GetAsync($"/api/projects/{project.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var entries = await _factory.AuditEntriesForActionAsync("project:read");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task Projects_delete_soft_cascades_memberships()
    {
        var team = await CreateTeamAsync("DelTeam");
        var project = await CreateProjectAsync(team.Id);
        var (_, aliceId) = await _factory.LoginFreshMemberAsync($"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        await _operator.PostAsJsonAsync($"/api/projects/{project.Id}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });

        var resp = await _operator.DeleteAsync($"/api/projects/{project.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Memberships are soft-cascaded; the list endpoint returns 200 with an empty array
        // because the soft delete filters them out via the global query filter.
        var list = await _operator.GetAsync($"/api/projects/{project.Id}/memberships");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        listBody.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    // ----- Memberships -----------------------------------------------------

    [Fact]
    public async Task Memberships_add_as_operator_returns_200_with_audit()
    {
        var team = await CreateTeamAsync("MTeam");
        var project = await CreateProjectAsync(team.Id);
        var (_, aliceId) = await _factory.LoginFreshMemberAsync($"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{project.Id}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _factory.AuditEntriesForActionAsync("project:membership:add");
        entries.Should().Contain(e => e.Outcome == AuditOutcome.Granted && e.ProjectId == project.Id);
    }

    [Fact]
    public async Task Memberships_add_duplicate_returns_409()
    {
        var team = await CreateTeamAsync("MDup");
        var project = await CreateProjectAsync(team.Id);
        var (_, aliceId) = await _factory.LoginFreshMemberAsync($"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");
        await _operator.PostAsJsonAsync($"/api/projects/{project.Id}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });

        var resp = await _operator.PostAsJsonAsync($"/api/projects/{project.Id}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Memberships_add_as_non_operator_returns_403()
    {
        var team = await CreateTeamAsync("MNo");
        var project = await CreateProjectAsync(team.Id);
        var (alice, aliceId) = await _factory.LoginFreshMemberAsync($"a-{Guid.NewGuid():N}@test.local", "Pw_A_123!", "Alice");

        var resp = await alice.PostAsJsonAsync($"/api/projects/{project.Id}/memberships", new
        {
            memberId = aliceId,
            roleKeys = new[] { "operator" },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- Roles -----------------------------------------------------------

    [Fact]
    public async Task Roles_list_returns_seeded_operator_role()
    {
        var resp = await _operator.GetAsync("/api/roles");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var keys = body.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("key").GetString());
        keys.Should().Contain("operator");
    }

    [Fact]
    public async Task Roles_list_anonymous_returns_401()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ----- helpers ---------------------------------------------------------

    private async Task<TeamRef> CreateTeamAsync(string namePrefix)
    {
        var name = $"{namePrefix}-{Guid.NewGuid():N}";
        var resp = await _operator.PostAsJsonAsync("/api/teams", new { name });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new TeamRef(body.GetProperty("data").GetProperty("id").GetGuid(), name);
    }

    private async Task<ProjectRef> CreateProjectAsync(Guid teamId)
    {
        var slug = $"p-{Guid.NewGuid():N}".Substring(0, 14);
        var resp = await _operator.PostAsJsonAsync("/api/projects", new
        {
            name = $"Proj-{Guid.NewGuid():N}",
            slug,
            projectType = "feature-delivery",
            owningTeamId = teamId,
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new ProjectRef(body.GetProperty("data").GetProperty("id").GetGuid(), slug);
    }

    private sealed record TeamRef(Guid Id, string Name);
    private sealed record ProjectRef(Guid Id, string Slug);
}
