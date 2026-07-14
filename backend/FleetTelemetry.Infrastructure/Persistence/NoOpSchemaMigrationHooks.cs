using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Infrastructure.Persistence;

public sealed class NoOpSchemaMigrationHooks : ISchemaMigrationHooks
{
    public static NoOpSchemaMigrationHooks Instance { get; } = new();

    public Task OnBackfillStartingAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task OnBeforeRegisterVersionAsync(int version, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
