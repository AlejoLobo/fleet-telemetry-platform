using FleetTelemetry.Api.Controllers;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Application.Tests.TestHelpers;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

// FT-004: validación estricta de cursores en endpoints.
public class CursorValidationTests
{
    private static readonly Guid DeviceA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Fleet_cursor_sin_LastDeviceId_retorna_400()
    {
        var cursor = CursorCodec.Encode(new FleetCursorPayload(1, Guid.Empty, false, true));
        var controller = CreateFleetController([]);

        var result = await controller.GetAll(pageSize: 10, cursor: cursor, cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Fleet_cursor_con_LastDeviceId_vacio_retorna_400()
    {
        // Cursor JSON legado con LastDeviceId vacío se decodifica como Guid.Empty.
        var cursor = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    """{"version":1,"lastDeviceId":"00000000-0000-0000-0000-000000000000","liveOnly":false,"excludeSimulated":true}"""))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var controller = CreateFleetController([]);

        var result = await controller.GetAll(pageSize: 10, cursor: cursor, cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task History_cursor_sin_LastTimestamp_retorna_400()
    {
        var from = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(2);
        var cursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(1, DeviceA, from, to, null, Guid.NewGuid()));
        var controller = CreateTelemetryController();

        var result = await controller.GetByVehicle(
            DeviceA,
            from: null,
            to: null,
            pageSize: 10,
            cursor: cursor,
            telemetryRepository: new FakeTelemetryRepository(),
            timeProvider: TimeProvider.System,
            queryLimits: Options.Create(new QueryLimitsOptions()),
            cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task History_cursor_sin_LastEventId_retorna_400()
    {
        var from = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(2);
        var cursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(1, DeviceA, from, to, to, null));
        var controller = CreateTelemetryController();

        var result = await controller.GetByVehicle(
            DeviceA,
            from: null,
            to: null,
            pageSize: 10,
            cursor: cursor,
            telemetryRepository: new FakeTelemetryRepository(),
            timeProvider: TimeProvider.System,
            queryLimits: Options.Create(new QueryLimitsOptions()),
            cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task History_cursor_con_Guid_Empty_retorna_400()
    {
        var from = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(2);
        var cursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(1, DeviceA, from, to, to, Guid.Empty));
        var controller = CreateTelemetryController();

        var result = await controller.GetByVehicle(
            DeviceA,
            from: null,
            to: null,
            pageSize: 10,
            cursor: cursor,
            telemetryRepository: new FakeTelemetryRepository(),
            timeProvider: TimeProvider.System,
            queryLimits: Options.Create(new QueryLimitsOptions()),
            cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Cursor_con_propiedad_desconocida_retorna_400()
    {
        var unknownPayloadCursor = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $$"""{"version":1,"lastDeviceId":"{{DeviceA:D}}","liveOnly":false,"excludeSimulated":true,"extra":true}"""))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var controller = CreateFleetController([]);

        var result = await controller.GetAll(pageSize: 10, cursor: unknownPayloadCursor, cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Cursor_demasiado_grande_retorna_400()
    {
        var oversized = new string('A', CursorCodec.MaxCursorLength + 1);
        var controller = CreateFleetController([]);

        var result = await controller.GetAll(pageSize: 10, cursor: oversized, cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Endpoint_cursor_con_from_distinto_retorna_400()
    {
        var from = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(2);
        var cursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(1, DeviceA, from, to, to, Guid.NewGuid()));
        var controller = CreateTelemetryController();

        var result = await controller.GetByVehicle(
            DeviceA,
            from: from.AddHours(1),
            to: to,
            pageSize: 10,
            cursor: cursor,
            telemetryRepository: new FakeTelemetryRepository(),
            timeProvider: TimeProvider.System,
            queryLimits: Options.Create(new QueryLimitsOptions()),
            cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Endpoint_cursor_con_to_distinto_retorna_400()
    {
        var from = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(2);
        var cursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(1, DeviceA, from, to, to, Guid.NewGuid()));
        var controller = CreateTelemetryController();

        var result = await controller.GetByVehicle(
            DeviceA,
            from: from,
            to: to.AddHours(1),
            pageSize: 10,
            cursor: cursor,
            telemetryRepository: new FakeTelemetryRepository(),
            timeProvider: TimeProvider.System,
            queryLimits: Options.Create(new QueryLimitsOptions()),
            cancellationToken: CancellationToken.None);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task Ningun_cursor_invalido_repite_la_primera_pagina()
    {
        var vehicles = Enumerable.Range(1, 5)
            .Select(i =>
            {
                var deviceId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}");
                return new VehicleLatestStatusResponse(
                    deviceId,
                    deviceId.ToString("D"),
                    "online",
                    DateTimeOffset.UtcNow,
                    10,
                    1,
                    1,
                    null,
                    "gps");
            })
            .ToList();

        var fleetQuery = new FakeFleetQueryService(vehicles);
        var firstPage = await fleetQuery.GetFleetPageAsync(2, null);

        var invalidCursors = new[]
        {
            CursorCodec.Encode(new FleetCursorPayload(1, Guid.Empty, false, true)),
            new string('Z', CursorCodec.MaxCursorLength + 10),
        };

        foreach (var invalidCursor in invalidCursors)
        {
            await Assert.ThrowsAsync<InvalidCursorException>(() =>
                fleetQuery.GetFleetPageAsync(2, invalidCursor));
        }

        var secondValidPage = await fleetQuery.GetFleetPageAsync(2, firstPage.NextCursor);
        Assert.DoesNotContain(secondValidPage.Items, item => firstPage.Items.Any(f => f.DeviceId == item.DeviceId));
    }

    private static FleetController CreateFleetController(IReadOnlyList<VehicleLatestStatusResponse> vehicles) =>
        new(new FakeFleetQueryService(vehicles), Options.Create(new QueryLimitsOptions()));

    private static TelemetryController CreateTelemetryController() =>
        new(null!, null!);

    private static void AssertBadRequest(ActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    private static void AssertBadRequest<T>(ActionResult<T> result) =>
        AssertBadRequest((ActionResult)result.Result!);

    private sealed class FakeTelemetryRepository : ITelemetryRepository
    {
        public Task SaveAsync(Domain.Entities.TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CursorPage<Domain.Entities.TelemetryEvent>> GetVehicleHistoryPageAsync(
            Guid deviceId,
            DateTimeOffset from,
            DateTimeOffset to,
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CursorPage<Domain.Entities.TelemetryEvent>([], null, false));
    }
}
