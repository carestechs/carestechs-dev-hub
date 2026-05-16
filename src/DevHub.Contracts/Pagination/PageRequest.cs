namespace DevHub.Contracts.Pagination;

/// <summary>
/// Standard pagination input for list endpoints. Bound via <c>[FromQuery]</c>; callers
/// invoke <see cref="Normalize"/> to clamp out-of-range values before passing to services.
/// </summary>
public sealed record PageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }

    public PageRequest Normalize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, 100),
        SortDir = SortDir?.ToLowerInvariant() switch
        {
            "asc" => "asc",
            "desc" => "desc",
            _ => null,
        },
    };
}
