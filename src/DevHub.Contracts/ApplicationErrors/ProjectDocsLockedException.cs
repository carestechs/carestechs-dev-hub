namespace DevHub.Contracts.ApplicationErrors;

/// <summary>
/// Thrown when a doc section update is attempted on a project whose docs are
/// fully filled. All future updates must go through a work item.
/// </summary>
public sealed class ProjectDocsLockedException()
    : DomainException(
        title: "Project docs are locked",
        detail: "All required doc sections are already filled. Submit a work item to request doc updates.",
        status: 409,
        type: "/probs/project-docs-locked");
