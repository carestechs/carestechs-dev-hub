namespace DevHub.Contracts.Workspace;

public interface IProjectDocsQuery
{
    /// <summary>
    /// Returns whether all required documents for the project are filled, and which keys are missing.
    /// </summary>
    Task<(bool AllFilled, IReadOnlyList<string> MissingKeys)> CheckAllFilledAsync(Guid projectId, CancellationToken ct);
}
