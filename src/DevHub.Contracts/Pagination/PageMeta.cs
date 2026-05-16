namespace DevHub.Contracts.Pagination;

public sealed record PageMeta(
    int TotalCount,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDir);
