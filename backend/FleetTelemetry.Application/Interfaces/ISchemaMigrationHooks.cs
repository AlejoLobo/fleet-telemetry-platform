namespace FleetTelemetry.Application.Interfaces;

// Seam inyectable para pruebas de migraciones de esquema.
public interface ISchemaMigrationHooks
{
    Task OnBackfillStartingAsync(CancellationToken cancellationToken = default);

    Task OnBeforeRegisterVersionAsync(int version, CancellationToken cancellationToken = default);
}
