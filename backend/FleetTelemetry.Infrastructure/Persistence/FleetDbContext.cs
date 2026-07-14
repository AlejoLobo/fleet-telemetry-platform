using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

// Contexto Entity Framework para TimescaleDB.
namespace FleetTelemetry.Infrastructure.Persistence;

// Mapea entidades de telemetría, alertas e idempotencia.
public class FleetDbContext : DbContext
{
    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options)
    {
    }

    public DbSet<TelemetryEventRecord> TelemetryEvents => Set<TelemetryEventRecord>();
    public DbSet<ProcessedEventRecord> ProcessedEvents => Set<ProcessedEventRecord>();
    public DbSet<FleetAlertRecord> FleetAlerts => Set<FleetAlertRecord>();
    public DbSet<FleetAlertConditionStateRecord> FleetAlertStates => Set<FleetAlertConditionStateRecord>();
    public DbSet<FleetVehicleStateRecord> FleetVehicleStates => Set<FleetVehicleStateRecord>();
    public DbSet<FleetConnectivityWatermarkRecord> FleetConnectivityWatermarks => Set<FleetConnectivityWatermarkRecord>();
    public DbSet<FleetOfflinePublishMarkerRecord> FleetOfflinePublishMarkers => Set<FleetOfflinePublishMarkerRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryEventRecord>(entity =>
        {
            entity.HasKey(e => new { e.EventId, e.Timestamp });
            entity.HasIndex(e => new { e.VehicleId, e.Timestamp, e.EventId });
        });

        modelBuilder.Entity<ProcessedEventRecord>(entity =>
        {
            entity.HasIndex(e => e.ProcessedAt);
        });

        modelBuilder.Entity<FleetAlertRecord>(entity =>
        {
            entity.HasIndex(e => new { e.VehicleId, e.CreatedAt });
            entity.HasIndex(e => e.IsAcknowledged);
        });

        modelBuilder.Entity<FleetAlertConditionStateRecord>(entity =>
        {
            entity.HasKey(e => new { e.VehicleId, e.AlertType });
            entity.HasIndex(e => new { e.IsActive, e.LastConditionAt });
        });

        modelBuilder.Entity<FleetVehicleStateRecord>(entity =>
        {
            entity.HasIndex(e => e.LastTimestamp);
            entity.HasIndex(e => new { e.LocationSource, e.LastTimestamp });
            entity.HasIndex(e => new { e.LastTimestamp, e.VehicleId });
        });

        modelBuilder.Entity<FleetConnectivityWatermarkRecord>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}
