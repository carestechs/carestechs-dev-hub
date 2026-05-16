using System.Net.Http.Json;
using DevHub.TestHarness.FakeExecutor;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.WorkItems.Tests;

/// Smoke tests for the FakeExecutorHost itself — verifies the host binds to a random port,
/// echoes scripted responses, and records calls. The real façade tests come in T-038.
public class FakeExecutorSmokeTests
{
    [Fact]
    public async Task Boots_on_random_port_and_responds_to_start()
    {
        await using var fake = await FakeExecutorHost.StartAsync();
        fake.BaseUrl.Should().StartWith("http://127.0.0.1:");

        using var http = new HttpClient { BaseAddress = new Uri(fake.BaseUrl) };
        var resp = await http.PostAsJsonAsync("/work-items", new { input = new { } });
        resp.EnsureSuccessStatusCode();

        fake.Total.Should().Be(1);
        fake.CountByPath("/work-items").Should().Be(1);
        fake.Calls.TryPeek(out var call).Should().BeTrue();
        call!.Method.Should().Be("POST");
    }

    [Fact]
    public async Task Stream_endpoint_writes_chunks_with_delay_and_decrements_counter()
    {
        await using var fake = await FakeExecutorHost.StartAsync();
        fake.Scripted.StreamChunks = new List<(string, TimeSpan)>
        {
            ("data: a\n\n", TimeSpan.Zero),
            ("data: b\n\n", TimeSpan.FromMilliseconds(40)),
        };

        using var http = new HttpClient { BaseAddress = new Uri(fake.BaseUrl) };
        using var req = new HttpRequestMessage(HttpMethod.Get, "/work-items/abc/stream");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync();

        body.Should().Contain("data: a").And.Contain("data: b");
        // After the request completes, the connection counter must be back to 0.
        fake.OpenStreamConnections.Should().Be(0);
    }

    [Fact]
    public async Task Captures_authorization_header()
    {
        await using var fake = await FakeExecutorHost.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(fake.BaseUrl) };
        using var req = new HttpRequestMessage(HttpMethod.Get, "/work-items/abc");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "shh-secret");
        (await http.SendAsync(req)).EnsureSuccessStatusCode();

        fake.Calls.TryPeek(out var call).Should().BeTrue();
        call!.Authorization.Should().Be("Bearer shh-secret");
    }
}
