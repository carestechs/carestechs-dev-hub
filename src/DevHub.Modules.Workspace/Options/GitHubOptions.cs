namespace DevHub.Modules.Workspace.Options;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Pat { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;

    /// Optional: "owner/repo" of the scaffold source. When set, the contents of its
    /// "scaffold/" folder are copied into every newly created GitHub repo.
    public string ScaffoldRepo { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Pat) && !string.IsNullOrWhiteSpace(Owner);
}
