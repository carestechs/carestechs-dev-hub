namespace DevHub.Contracts.ApplicationErrors;

public sealed class ProjectDocsIncompleteException(IReadOnlyList<string> missingKeys)
    : DomainException(
        title: "Project Documentation Incomplete",
        detail: "All project documents must be filled before creating work items.",
        status: 409,
        type: "/probs/project-docs-incomplete",
        errors: new Dictionary<string, string[]> { ["missingDocs"] = [.. missingKeys] })
{
    public IReadOnlyList<string> MissingKeys { get; } = missingKeys;
}
