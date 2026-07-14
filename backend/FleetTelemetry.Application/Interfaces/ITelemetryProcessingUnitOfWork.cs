using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public enum ProcessTelemetryOutcome
{
    Processed,
    Duplicate
}

/// <summary>
/// Persiste telemetría y alertas con idempotencia en una sola transacción.
/// </summary>
// Transacción atómica: evento, alertas e idempotencia.
public interface ITelemetryProcessingUnitOfWork
{
    Task<ProcessTelemetryOutcome> ProcessAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default);
}
