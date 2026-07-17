using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Application.Tests.TestHelpers;

internal sealed class FakeFleetQueryService(IReadOnlyList<VehicleLatestStatusResponse> vehicles) : IFleetQueryService
{
    public Task<CursorPage<VehicleLatestStatusResponse>> GetFleetPageAsync(
        int pageSize,
        string? cursor,
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default)
    {
        var filtered = vehicles.AsEnumerable();
        if (excludeSimulated)
            filtered = filtered.Where(v => v.LastLocationSource != "simulated");
        if (liveOnly)
            filtered = filtered.Where(v => v.Status == "online");

        var ordered = filtered.OrderBy(v => v.DeviceId).ToList();
        var startIndex = 0;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var payload = FleetTelemetry.Application.Services.CursorCodec.Decode<FleetCursorPayload>(cursor);
            FleetTelemetry.Application.Services.CursorValidators.ValidateFleetCursor(payload, liveOnly, excludeSimulated);
            startIndex = ordered.FindIndex(v => v.DeviceId.CompareTo(payload.LastDeviceId) > 0);
            if (startIndex < 0) startIndex = ordered.Count;
        }

        var pageItems = ordered.Skip(startIndex).Take(pageSize).ToList();
        var hasMore = startIndex + pageSize < ordered.Count;
        string? nextCursor = null;
        if (hasMore && pageItems.Count > 0)
        {
            nextCursor = FleetTelemetry.Application.Services.CursorCodec.Encode(
                new FleetCursorPayload(FleetCursorPayload.CurrentVersion, pageItems[^1].DeviceId, liveOnly, excludeSimulated));
        }

        return Task.FromResult(new CursorPage<VehicleLatestStatusResponse>(pageItems, nextCursor, hasMore));
    }

    public Task<IReadOnlyList<VehicleLatestStatusResponse>> GetAllFleetStatusesAsync(
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default)
    {
        var filtered = vehicles.AsEnumerable();
        if (excludeSimulated)
            filtered = filtered.Where(v => v.LastLocationSource != "simulated");
        if (liveOnly)
            filtered = filtered.Where(v => v.Status == "online");
        return Task.FromResult<IReadOnlyList<VehicleLatestStatusResponse>>(filtered.ToList());
    }

    public Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(vehicles.FirstOrDefault(v => v.DeviceId == deviceId));
}

internal sealed class FakeFleetStateAggregateRepository(
    FleetAggregateSnapshot snapshot,
    int criticalAlerts) : IFleetStateAggregateRepository
{
    public Task<FleetAggregateSnapshot> GetFleetAggregateSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshot);

    public Task<int> CountOpenCriticalAlertsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(criticalAlerts);
}
