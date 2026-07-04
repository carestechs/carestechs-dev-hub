using DevHub.Contracts.Persistence;

namespace DevHub.Modules.Workspace.Entities;

public sealed class DocTemplateVersion : BaseEntity
{
    public required int VersionNumber { get; set; }
    public required bool IsActive { get; set; }
    public string? Notes { get; set; }
}
