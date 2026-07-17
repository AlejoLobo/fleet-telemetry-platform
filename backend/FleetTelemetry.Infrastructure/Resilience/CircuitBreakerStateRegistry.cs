using System.Collections.Concurrent;
using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Infrastructure.Resilience;

// Implementación thread-safe de ICircuitBreakerStateRegistry.
public sealed class CircuitBreakerStateRegistry : ICircuitBreakerStateRegistry
{
    private readonly ConcurrentDictionary<ResilienceDependency, CircuitBreakerSnapshot> _snapshots = new();

    public CircuitBreakerStateRegistry()
    {
        foreach (ResilienceDependency dependency in Enum.GetValues<ResilienceDependency>())
        {
            _snapshots[dependency] = CreateSnapshot(dependency, CircuitBreakerState.Closed, 0);
        }
    }

    public void RecordTransition(ResilienceDependency dependency, CircuitBreakerState state)
    {
        _snapshots.AddOrUpdate(
            dependency,
            CreateSnapshot(dependency, state, 0),
            (_, current) => current with
            {
                State = state.ToString(),
                LastStateChangeUtc = DateTimeOffset.UtcNow,
                FailureCountInWindow = state == CircuitBreakerState.Closed ? 0 : current.FailureCountInWindow
            });
    }

    public void RecordFailure(ResilienceDependency dependency)
    {
        _snapshots.AddOrUpdate(
            dependency,
            CreateSnapshot(dependency, CircuitBreakerState.Closed, 1),
            (_, current) => current with { FailureCountInWindow = current.FailureCountInWindow + 1 });
    }

    public IReadOnlyList<CircuitBreakerSnapshot> GetSnapshots() =>
        _snapshots.Values
            .OrderBy(s => s.Dependency, StringComparer.Ordinal)
            .ToList();

    public bool IsOpen(ResilienceDependency dependency) =>
        _snapshots.TryGetValue(dependency, out var snapshot)
        && snapshot.State == CircuitBreakerState.Open.ToString();

    private static CircuitBreakerSnapshot CreateSnapshot(
        ResilienceDependency dependency,
        CircuitBreakerState state,
        int failures) =>
        new(dependency.ToString(), state.ToString(), DateTimeOffset.UtcNow, failures);
}
