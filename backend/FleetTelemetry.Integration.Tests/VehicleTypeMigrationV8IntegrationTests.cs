using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.ValueObjects;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

/// <summary>
/// Valida migración v8: columna vehicle_type con default car y catálogo cerrado.
/// </summary>
public class VehicleTypeMigrationV8IntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(_database.ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDeviceRegistry, FleetTelemetry.Infrastructure.Repositories.TimescaleDeviceRegistry>();
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Fresh_database_has_vehicle_type_default_car()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        var deviceId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var device = await registry.RegisterDeviceAsync(deviceId);

        Assert.Equal(VehicleType.CarCode, device.VehicleType);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT vehicle_type FROM fleet_devices WHERE device_id = @id;";
        cmd.Parameters.AddWithValue("id", deviceId);
        var stored = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("car", stored);
    }

    [Fact]
    public async Task Legacy_rows_without_type_receive_car_on_migration()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        // Simula esquema anterior: quita constraint y columna, luego re-aplica ensure.
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = """
                ALTER TABLE fleet_devices DROP CONSTRAINT IF EXISTS "CK_fleet_devices_vehicle_type";
                ALTER TABLE fleet_devices DROP COLUMN IF EXISTS vehicle_type;
                DELETE FROM schema_versions WHERE "Version" = 8;
                """;
            await drop.ExecuteNonQueryAsync();
        }

        var legacyId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                VALUES (@id, 'VH-LEGACY', NOW(), NOW());
                """;
            seed.Parameters.AddWithValue("id", legacyId);
            await seed.ExecuteNonQueryAsync();
        }

        await DatabaseInitializer.InitializeAsync(_services);

        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT vehicle_type, vehicle_name FROM fleet_devices WHERE device_id = @id;";
            read.Parameters.AddWithValue("id", legacyId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("car", reader.GetString(0));
            Assert.Equal("VH-LEGACY", reader.GetString(1));
        }
    }

    [Fact]
    public async Task Persists_motorcycle_and_rejects_invalid_type()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        var deviceId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();

        var created = await registry.RegisterDeviceAsync(deviceId, "motorcycle");
        Assert.Equal("motorcycle", created.VehicleType);
        Assert.Equal(deviceId, created.DeviceId);

        var updated = await registry.UpdateDeviceProfileAsync(deviceId, vehicleName: null, vehicleType: "van");
        Assert.Equal("van", updated.VehicleType);
        Assert.Equal(created.VehicleName, updated.VehicleName);

        await Assert.ThrowsAsync<InvalidVehicleTypeException>(() =>
            registry.UpdateDeviceProfileAsync(deviceId, vehicleName: null, vehicleType: "boat"));
    }

    [Fact]
    public async Task Register_idempotent_keeps_existing_type()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        var deviceId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();

        var first = await registry.RegisterDeviceAsync(deviceId, "bus");
        var second = await registry.RegisterDeviceAsync(deviceId, "truck");

        Assert.Equal("bus", first.VehicleType);
        Assert.Equal("bus", second.VehicleType);
        Assert.Equal(first.VehicleName, second.VehicleName);
        Assert.Equal(deviceId, second.DeviceId);
    }

    [Fact]
    public async Task Schema_version_8_and_check_constraint_are_present()
    {
        await DatabaseInitializer.InitializeAsync(_services);
        await DatabaseInitializer.InitializeAsync(_services); // idempotente

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = """SELECT MAX("Version") FROM schema_versions;""";
            var maxVersion = Convert.ToInt32(await versionCmd.ExecuteScalarAsync());
            Assert.True(maxVersion >= 8, $"Expected schema version >= 8, got {maxVersion}");
        }

        await using (var constraintCmd = connection.CreateCommand())
        {
            constraintCmd.CommandText = """
                SELECT pg_get_constraintdef(oid)
                FROM pg_constraint
                WHERE conname = 'CK_fleet_devices_vehicle_type'
                  AND conrelid = 'fleet_devices'::regclass;
                """;
            var definition = (string?)await constraintCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrWhiteSpace(definition));
            foreach (var code in new[] { "car", "motorcycle", "van", "truck", "bus", "pickup" })
                Assert.Contains(code, definition!, StringComparison.Ordinal);
        }

        await using (var invalidInsert = connection.CreateCommand())
        {
            invalidInsert.CommandText = """
                INSERT INTO fleet_devices (device_id, vehicle_name, vehicle_type, created_at, updated_at)
                VALUES (gen_random_uuid(), 'VH-INVALID', 'boat', NOW(), NOW());
                """;
            var ex = await Assert.ThrowsAsync<PostgresException>(() => invalidInsert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        }
    }
}
