using DevHub.Contracts.ApplicationErrors;
using DevHub.Contracts.Validation;
using FluentAssertions;

namespace DevHub.Contracts.Tests.Validation;

public class CodeSourceValidatorTests
{
    // ---------- ValidateRepo: accepts ----------

    [Theory]
    [InlineData("acme/widgets")]
    [InlineData("my-org/repo.with.dots")]
    [InlineData("a/b")]
    [InlineData("A_b/C-d.e")]
    [InlineData("carestechs/carestechs-dev-hub")]
    public void ValidateRepo_accepts_valid(string repo) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateRepo(repo))
            .Should().NotThrow();

    // ---------- ValidateRepo: rejects ----------

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

    [Fact]
    public void ValidateRepo_error_carries_rule_name_and_value()
    {
        var act = () => CodeSourceValidator.ValidateRepo("https://github.com/foo/bar");
        var ex = act.Should().Throw<ValidationException>().Which;
        ex.Errors!.Should().ContainKey("repo");
        ex.Errors!["repo"].Single().Should().Contain("repo.shape").And.Contain("https://github.com/foo/bar");
    }

    [Fact]
    public void ValidateRepo_uses_supplied_field_name()
    {
        var act = () => CodeSourceValidator.ValidateRepo("nope", fieldName: "project.repo");
        var ex = act.Should().Throw<ValidationException>().Which;
        ex.Errors!.Should().ContainKey("project.repo");
    }

    // ---------- ValidateBranch: accepts ----------

    [Theory]
    [InlineData("main")]
    [InlineData("feat/imp-042")]
    [InlineData("release/v1.2.3")]
    [InlineData("hotfix/x")]
    [InlineData("dependabot/npm_and_yarn/foo-1.2.3")]
    public void ValidateBranch_accepts_valid(string branch) =>
        FluentActions.Invoking(() => CodeSourceValidator.ValidateBranch(branch))
            .Should().NotThrow();

    // ---------- ValidateBranch: rejects ----------

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

    [Fact]
    public void ValidateBranch_error_carries_rule_name()
    {
        var act = () => CodeSourceValidator.ValidateBranch("/main");
        var ex = act.Should().Throw<ValidationException>().Which;
        ex.Errors!.Should().ContainKey("branch");
        ex.Errors!["branch"].Single().Should().Contain("branch.leadingSlash");
    }

    [Fact]
    public void ValidateBranch_uses_supplied_field_name()
    {
        var act = () => CodeSourceValidator.ValidateBranch("/x", fieldName: "workBranch");
        var ex = act.Should().Throw<ValidationException>().Which;
        ex.Errors!.Should().ContainKey("workBranch");
    }
}
