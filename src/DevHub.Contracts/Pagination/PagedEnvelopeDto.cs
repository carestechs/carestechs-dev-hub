namespace DevHub.Contracts.Pagination;

/// <summary>
/// Paginated success envelope: <c>{ "data": [...], "meta": { … } }</c>.
/// </summary>
public sealed record PagedEnvelopeDto<T>(IReadOnlyList<T> Data, PageMeta Meta);
