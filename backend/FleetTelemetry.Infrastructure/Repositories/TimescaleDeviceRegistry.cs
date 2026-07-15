using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetTelemetry.Infrastructure.Repositories;

/// <summary>
/// Registro de dispositivos en Timescale/Postgres: identidad estable + nombre VH-### atómico.
/// </summary>
public sealed class TimescaleDeviceRegistry : IDeviceRegistry
{
    private const int MaxNameAllocationAttempts = 64;
    private readonly FleetDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TimescaleDeviceRegistry(FleetDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<FleetDevice?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (!DeviceId.TryCreate(deviceId, out _, out _))
            return null;

        var record = await _dbContext.FleetDevices
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);

        return record is null ? null : ToDomain(record);
    }

    public async Task<FleetDevice> RegisterDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (!DeviceId.TryCreate(deviceId, out _, out var deviceError))
            throw new InvalidDeviceIdException(deviceError ?? "DeviceId is required.");

        var existing = await GetDeviceAsync(deviceId, cancellationToken);
        if (existing is not null)
            return existing;

        for (var attempt = 0; attempt < MaxNameAllocationAttempts; attempt++)
        {
            existing = await GetDeviceAsync(deviceId, cancellationToken);
            if (existing is not null)
                return existing;

            var sequenceValue = await NextVehicleNameSequenceAsync(cancellationToken);
            var vehicleName = VehicleName.FormatAutomatic(sequenceValue);
            var now = _timeProvider.GetUtcNow();

            try
            {
                var inserted = await TryInsertDeviceAsync(deviceId, vehicleName, now, cancellationToken);
                if (inserted is not null)
                    return inserted;

                // Otro proceso insertó el mismo DeviceId concurrentemente.
                existing = await GetDeviceAsync(deviceId, cancellationToken);
                if (existing is not null)
                    return existing;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                existing = await GetDeviceAsync(deviceId, cancellationToken);
                if (existing is not null)
                    return existing;

                // Nombre ocupado (p. ej. VH-### tomado por rename); reintentar con el siguiente.
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                existing = await GetDeviceAsync(deviceId, cancellationToken);
                if (existing is not null)
                    return existing;
            }
        }

        throw new InvalidOperationException(
            $"Unable to allocate a unique vehicle name for device '{deviceId}' after {MaxNameAllocationAttempts} attempts.");
    }

    public async Task<FleetDevice> RenameDeviceAsync(
        Guid deviceId,
        string vehicleName,
        CancellationToken cancellationToken = default)
    {
        if (!DeviceId.TryCreate(deviceId, out _, out var deviceError))
            throw new InvalidDeviceIdException(deviceError ?? "DeviceId is required.");

        if (!VehicleName.TryCreate(vehicleName, out var normalizedName, out var nameError))
            throw new InvalidVehicleNameException(nameError ?? "VehicleName is invalid.");

        var record = await _dbContext.FleetDevices
            .SingleOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);

        if (record is null)
            throw new DeviceNotFoundException(deviceId);

        if (string.Equals(record.VehicleName, normalizedName!.Value, StringComparison.Ordinal))
            return ToDomain(record);

        var conflict = await _dbContext.FleetDevices
            .AsNoTracking()
            .AnyAsync(
                d => d.VehicleName == normalizedName.Value && d.DeviceId != deviceId,
                cancellationToken);

        if (conflict)
            throw new VehicleNameConflictException(normalizedName.Value);

        var now = _timeProvider.GetUtcNow();
        record.VehicleName = normalizedName.Value;
        record.UpdatedAt = now;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new VehicleNameConflictException(normalizedName.Value);
        }

        return ToDomain(record);
    }

    private async Task<long> NextVehicleNameSequenceAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Database
            .SqlQueryRaw<SequenceValueRow>("SELECT nextval('fleet_vehicle_name_seq') AS \"Value\"")
            .ToListAsync(cancellationToken);

        return rows[0].Value;
    }

    private sealed class SequenceValueRow
    {
        public long Value { get; set; }
    }

    private async Task<FleetDevice?> TryInsertDeviceAsync(
        Guid deviceId,
        string vehicleName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
            VALUES ({deviceId}, {vehicleName}, {now}, {now})
            ON CONFLICT (device_id) DO NOTHING
            """,
            cancellationToken);

        if (rows == 0)
            return null;

        return FleetDevice.FromPersistence(deviceId, vehicleName, now, now);
    }

    private static FleetDevice ToDomain(FleetDeviceRecord record) =>
        FleetDevice.FromPersistence(
            record.DeviceId,
            record.VehicleName,
            record.CreatedAt,
            record.UpdatedAt);

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
                return true;
        }

        return false;
    }
}
