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
    }

    public Task PublishVehicleUpdateAsync(
        string vehicleId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
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
