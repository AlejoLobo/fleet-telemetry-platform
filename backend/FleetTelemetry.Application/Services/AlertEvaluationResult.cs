using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Services;

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

public enum AlertConditionObservationStatus
{
    NotObserved = 0,
    Recovered = 1,
    Breached = 2
}

public readonly record struct AlertConditionObservation(
    string AlertType,
    string Severity,
    string Message,
    AlertConditionObservationStatus Status);
