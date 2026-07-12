using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Integration.Tests;

// Hooks de prueba inyectables para validar migraciones de esquema.
internal sealed class TestSchemaMigrationHooks : ISchemaMigrationHooks
{
    public int BackfillCount { get; private set; }

    public bool ThrowOnVersionRegister { get; set; }

    public int ThrowOnVersion { get; set; } = 3;

    public void Reset() => BackfillCount = 0;

    public Task OnBackfillStartingAsync(CancellationToken cancellationToken = default)
    {
        BackfillCount += 1;
        return Task.CompletedTask;
    }

    public Task OnBeforeRegisterVersionAsync(int version, CancellationToken cancellationToken = default)
    {
        if (ThrowOnVersionRegister && version == ThrowOnVersion)
            throw new InvalidOperationException($"Simulated failure registering version {version}.");

        return Task.CompletedTask;
    }
}
