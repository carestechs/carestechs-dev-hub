# Implementation Plan: T-056 — CodeSourceValidator + unit tests

## Task Reference
- **Task ID:** T-056 · **Type:** Backend · **Workflow:** standard · **Complexity:** S
- **Rationale:** AC-3, AC-4. Boundary-parity with the orchestrator's `intake.codeSource` schema, centralized.

## Overview
A static class in `DevHub.Contracts/Validation/` with two methods, throwing the project's `ValidationException` on bad input. Twenty-plus xUnit cases cover every reject and accept enumerated in the brief.

## Implementation Steps

### Step 1: Create the validator
**File:** `src/DevHub.Contracts/Validation/CodeSourceValidator.cs` · Create

```csharp
using System.Text.RegularExpressions;
using DevHub.Contracts.ApplicationErrors;

namespace DevHub.Contracts.Validation;

public static class CodeSourceValidator
{
    private static readonly Regex RepoShape =
        new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static void ValidateRepo(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            throw new ValidationException(Error("repo.empty", repo, "must not be empty"));
        if (!RepoShape.IsMatch(repo))
            throw new ValidationException(Error("repo.shape", repo,
                "must match 'owner/name' — no scheme, no '.git' suffix, no whitespace, no leading slash"));
    }

    public static void ValidateBranch(string branch)
    {
        if (string.IsNullOrEmpty(branch))
            throw new ValidationException(Error("branch.empty", branch, "must not be empty"));
        if (branch[0] == '/')
            throw new ValidationException(Error("branch.leadingSlash", branch, "must not start with '/'"));
        if (branch.Contains(".."))
            throw new ValidationException(Error("branch.dotDot", branch, "must not contain '..'"));
        foreach (var ch in branch)
        {
            if (char.IsWhiteSpace(ch))
                throw new ValidationException(Error("branch.whitespace", branch, "must not contain whitespace"));
            if (ch < 0x20 || ch == 0x7F)
                throw new ValidationException(Error("branch.controlChar", branch, "must not contain control characters"));
        }
    }

    private static string Error(string rule, string value, string detail)
    {
        var safe = value.Length <= 200 ? value : value[..200] + "…";
        return $"{rule}: '{safe}' — {detail}";
    }
}
```

### Step 2: Create the unit test file
**File:** `tests/DevHub.Contracts.Tests/Validation/CodeSourceValidatorTests.cs` · Create

```csharp
using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Validation;
using FluentAssertions;
using Xunit;

namespace DevHub.Contracts.Tests.Validation;

public class CodeSourceValidatorTests
{
    [Theory]
    [InlineData("acme/widgets")]
    [InlineData("my-org/repo.with.dots")]
    [InlineData("a/b")]
    [InlineData("A_b/C-d.e")]
    public void ValidateRepo_accepts_valid(string repo) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateRepo(repo)).Should().NotThrow();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://github.com/foo/bar")]
    [InlineData("foo/bar.git")]
    [InlineData("foo")]
    [InlineData("foo/bar/baz")]
    [InlineData("foo bar/baz")]
    [InlineData("/foo/bar")]
    public void ValidateRepo_rejects_invalid(string repo) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateRepo(repo))
            .Should().Throw<ValidationException>();

    [Theory]
    [InlineData("main")]
    [InlineData("feat/imp-042")]
    [InlineData("release/v1.2.3")]
    [InlineData("hotfix/x")]
    public void ValidateBranch_accepts_valid(string branch) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateBranch(branch)).Should().NotThrow();

    [Theory]
    [InlineData("")]
    [InlineData("/main")]
    [InlineData("feat/..x")]
    [InlineData("feat /x")]
    [InlineData("feat\tx")]
    [InlineData("feat\nx")]
    [InlineData("feat\x01x")]
    [InlineData("feat\x7Fx")]
    public void ValidateBranch_rejects_invalid(string branch) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateBranch(branch))
            .Should().Throw<ValidationException>();
}
```

### Step 3: Verify project membership
**File:** `tests/DevHub.Contracts.Tests/DevHub.Contracts.Tests.csproj` · Verify

Confirm the test project exists (mirror `tests/DevHub.Modules.Workspace.Tests` if not). If it doesn't exist, create it via `dotnet new xunit` + `dotnet add reference src/DevHub.Contracts` + add to the solution.

### Step 4: Run tests
**Bash:**

```bash
dotnet test tests/DevHub.Contracts.Tests
```

Expect 4 theories × multiple cases = 24+ tests, all green.

## Files Affected
| File | Action |
|------|--------|
| `src/DevHub.Contracts/Validation/CodeSourceValidator.cs` | Create |
| `tests/DevHub.Contracts.Tests/Validation/CodeSourceValidatorTests.cs` | Create |
| `tests/DevHub.Contracts.Tests/DevHub.Contracts.Tests.csproj` | Verify / Create |

## Edge Cases & Risks
- **Unicode in branch names.** Git allows them, but the orchestrator's spec says "no whitespace, no control chars" only — so we follow that. Non-ASCII printable chars (`é`, `中`) pass our validator. That matches the orchestrator.
- **Trailing whitespace on repo.** Rejected by `IsNullOrWhiteSpace` for the all-whitespace case; explicitly excluded from the regex character class for embedded whitespace. We do not trim — operator must enter clean values (matches the brief's "we do not trim" rule).
- **Empty branch via PATCH cleared-field path.** In T-058 / T-062 the "clear the work branch" flow sets the field back to `null`, not `""`. Validator is only called when the value is non-null.

## Acceptance Verification
- [ ] All reject cases throw `ValidationException` with a rule-tagged message.
- [ ] All accept cases pass cleanly.
- [ ] ≥ 20 xUnit cases total. `dotnet test` is green.
