using System.Text.Json;
using FleetTelemetry.Application.Interfaces;

namespace FleetTelemetry.Integration.Tests;

// Publicador realtime con registro de llamadas para pruebas FT-004.
internal sealed class FakeFleetRealtimePublisher : IFleetRealtimePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly List<VehicleUpdateCall> _vehicleUpdates = [];
    private readonly List<string> _alertPayloads = [];

    public bool FailNextVehiclePublish { get; set; }

    public string? FailOnVehicleId { get; set; }

    public IReadOnlyList<VehicleUpdateCall> VehicleUpdates
    {
        get
        {
            lock (_sync)
                return _vehicleUpdates.ToList();
        }
    }

    public IReadOnlyList<string> AlertPayloads
    {
        get
        {
            lock (_sync)
                return _alertPayloads.ToList();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _vehicleUpdates.Clear();
            _alertPayloads.Clear();
        }

        FailNextVehiclePublish = false;
        FailOnVehicleId = null;
    }

    public Task PublishVehicleUpdateAsync(
        string vehicleId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (FailNextVehiclePublish
            || (FailOnVehicleId is not null && vehicleId == FailOnVehicleId))
        {
            FailNextVehiclePublish = false;
            throw new InvalidOperationException("Simulated publish failure.");
        }

        lock (_sync)
        {
            _vehicleUpdates.Add(new VehicleUpdateCall(vehicleId, payloadJson));
        }

        return Task.CompletedTask;
    }

    public Task PublishAlertAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _alertPayloads.Add(payloadJson);
        }

        return Task.CompletedTask;
    }

    public T? DeserializeVehiclePayload<T>(string payloadJson) =>
        JsonSerializer.Deserialize<T>(payloadJson, JsonOptions);

    internal sealed record VehicleUpdateCall(string VehicleId, string PayloadJson);
}
