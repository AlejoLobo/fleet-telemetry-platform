using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Infrastructure.Persistence;

// Implementación por defecto sin efectos secundarios.
public sealed class NoOpSchemaMigrationHooks : ISchemaMigrationHooks
{
    public static NoOpSchemaMigrationHooks Instance { get; } = new();

    public Task OnBackfillStartingAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task OnBeforeRegisterVersionAsync(int version, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
