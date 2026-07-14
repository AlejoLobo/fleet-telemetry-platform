using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Services;

// Resultado de la máquina de estados: alertas emitidas + estados a persistir.
public sealed class AlertEvaluationResult
{
    public AlertEvaluationResult(
        IReadOnlyList<FleetAlert> emittedAlerts,
        IReadOnlyList<FleetAlertConditionState> statesToUpsert)
    {
        EmittedAlerts = emittedAlerts;
        StatesToUpsert = statesToUpsert;
    }

    public IReadOnlyList<FleetAlert> EmittedAlerts { get; }

    public IReadOnlyList<FleetAlertConditionState> StatesToUpsert { get; }
}

// Observación de umbral para un tipo de alerta.
public readonly record struct AlertConditionObservation(
    string AlertType,
    string Severity,
    string Message,
    bool IsBreached);
