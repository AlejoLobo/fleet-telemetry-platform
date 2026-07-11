namespace FleetTelemetry.Application.DTOs;

// Cursor estable para paginación determinista de alertas SSE.
public readonly record struct AlertStreamCursor(DateTimeOffset CreatedAt, Guid AlertId)
{
    public static AlertStreamCursor Origin { get; } = new(DateTimeOffset.MinValue, Guid.Empty);

    public bool IsAfter(AlertStreamCursor other) =>
        CreatedAt > other.CreatedAt
        || (CreatedAt == other.CreatedAt && AlertId.CompareTo(other.AlertId) > 0);

    public static AlertStreamCursor FromAlert(DateTimeOffset createdAt, Guid alertId) =>
        new(createdAt, alertId);
}
