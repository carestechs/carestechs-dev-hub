namespace DevHub.Contracts;

/// <summary>
/// Standard success envelope: every API success response is shaped as <c>{ "data": ..., "meta": ... }</c>.
/// </summary>
public sealed record EnvelopeDto<T>(T Data, object? Meta = null);
