using DevHub.Contracts.Workspace;

namespace DevHub.TestHarness;

/// <summary>
/// Test stub that reports all project docs as filled. Registered by default in
/// <see cref="DevHubApiFactory"/> so existing integration tests are not blocked by the
/// docs gate. Tests that specifically verify gate behaviour should provide their own stub
/// via <see cref="DevHubApiFactory.ServiceOverrides"/>.
/// </summary>
public sealed class FakeProjectDocsQuery : IProjectDocsQuery
{
    public Task<(bool AllFilled, IReadOnlyList<string> MissingKeys)> CheckAllFilledAsync(
        Guid projectId, CancellationToken ct)
        => Task.FromResult<(bool, IReadOnlyList<string>)>((true, []));
}
