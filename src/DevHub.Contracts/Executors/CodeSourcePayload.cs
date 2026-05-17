using System.Text.Json.Serialization;

namespace DevHub.Contracts.Executors;

/// <summary>
/// Forwarded to lifecycle executors as <c>intake.codeSource</c> on every work-item
/// start when the project carries both <c>repo</c> and <c>defaultBranch</c>.
///
/// Field names + casing match the upstream orchestrator's IMP-004 contract exactly.
/// <see cref="WorkBranch"/> is omitted from the JSON when null (per the spec: omit,
/// do not send as <c>null</c>).
/// </summary>
public sealed record CodeSourcePayload(
    [property: JsonPropertyName("repo")] string Repo,
    [property: JsonPropertyName("baseBranch")] string BaseBranch,
    [property: JsonPropertyName("workBranch"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? WorkBranch);
