namespace FleetTelemetry.Application.DTOs;

// Página paginada por cursor opaco.
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore);
