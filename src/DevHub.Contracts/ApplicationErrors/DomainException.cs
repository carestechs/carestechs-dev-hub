namespace DevHub.Contracts.ApplicationErrors;

/// <summary>
/// Base exception for any failure that should translate to an RFC 7807 problem
/// response. Subclasses encode the status code and the <c>type</c> URI; the
/// global handler maps them via <see cref="Status"/> and <see cref="Type"/>.
/// </summary>
public abstract class DomainException(string title, string detail, int status, string type, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail)
{
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public int Status { get; } = status;
    public string Type { get; } = type;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
