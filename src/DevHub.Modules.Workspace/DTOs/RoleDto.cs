namespace DevHub.Modules.Workspace.DTOs;

public sealed record RoleDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsSystem);
