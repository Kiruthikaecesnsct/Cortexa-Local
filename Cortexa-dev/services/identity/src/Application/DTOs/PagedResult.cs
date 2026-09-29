namespace Cortexa.Identity.Application.DTOs;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int Size,
    int Total);
