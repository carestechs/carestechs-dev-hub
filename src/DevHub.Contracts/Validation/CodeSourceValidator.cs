using System.Text.RegularExpressions;
using DevHub.Contracts.ApplicationErrors;

namespace DevHub.Contracts.Validation;

/// <summary>
/// Boundary validation for the `intake.codeSource` fields forwarded to lifecycle
/// executors on work-item start. Rules mirror the upstream orchestrator's schema
/// exactly (FEAT-008, sourced from carestechs-agent-orchestrator IMP-004).
///
/// On failure, throws <see cref="ValidationException"/> tagging the error against
/// the caller-supplied field name so the global problem-details translator can
/// surface field-level errors to API consumers.
/// </summary>
public static class CodeSourceValidator
{
    private static readonly Regex RepoShape =
        new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static void ValidateRepo(string repo, string fieldName = "repo")
    {
        if (string.IsNullOrWhiteSpace(repo))
            throw Fail(fieldName, "repo.empty", repo, "must not be empty");
        if (!RepoShape.IsMatch(repo))
            throw Fail(fieldName, "repo.shape", repo,
                "must match 'owner/name' — no scheme, no whitespace, no leading slash");
        if (repo.EndsWith(".git", StringComparison.Ordinal))
            throw Fail(fieldName, "repo.gitSuffix", repo, "must not end with '.git'");
    }

    public static void ValidateBranch(string branch, string fieldName = "branch")
    {
        if (string.IsNullOrEmpty(branch))
            throw Fail(fieldName, "branch.empty", branch, "must not be empty");
        if (branch[0] == '/')
            throw Fail(fieldName, "branch.leadingSlash", branch, "must not start with '/'");
        if (branch.Contains(".."))
            throw Fail(fieldName, "branch.dotDot", branch, "must not contain '..'");
        foreach (var ch in branch)
        {
            if (char.IsWhiteSpace(ch))
                throw Fail(fieldName, "branch.whitespace", branch, "must not contain whitespace");
            if (ch < 0x20 || ch == 0x7F)
                throw Fail(fieldName, "branch.controlChar", branch, "must not contain control characters");
        }
    }

    private static ValidationException Fail(string field, string rule, string value, string detail)
    {
        var safe = value.Length <= 200 ? value : value[..200] + "…";
        var message = $"{rule}: '{safe}' — {detail}";
        return new ValidationException(
            new Dictionary<string, string[]> { [field] = new[] { message } });
    }
}
