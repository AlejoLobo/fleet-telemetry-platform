using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetTelemetry.Infrastructure.Persistence;

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
    public DbSet<FleetDeviceRecord> FleetDevices => Set<FleetDeviceRecord>();

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

        modelBuilder.Entity<FleetDeviceRecord>(entity =>
        {
            entity.ToTable("fleet_devices");
            entity.HasKey(e => e.DeviceId);
            entity.HasIndex(e => e.VehicleName).IsUnique();
            entity.Property(e => e.DeviceId).HasColumnName("device_id");
            entity.Property(e => e.VehicleName).HasColumnName("vehicle_name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
