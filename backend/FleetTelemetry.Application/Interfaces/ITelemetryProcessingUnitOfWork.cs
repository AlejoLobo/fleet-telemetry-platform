using FleetTelemetry.Domain.Entities;

// Contrato de unidad de trabajo para procesar telemetría.
namespace FleetTelemetry.Application.Interfaces;

// Resultado del procesamiento de un evento.
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
    // Procesa evento o detecta duplicado.
    Task<ProcessTelemetryOutcome> ProcessAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default);
}
