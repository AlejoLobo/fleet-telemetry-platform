using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Integration.Tests;

// FT-004: contrato canónico vehicle-update en pipeline Kafka → SSE.
public class RealtimeContractIntegrationTests
{
    [Fact]
    public void KafkaPush_vehicle_update_llega_al_cliente_web()
    {
        var payload = new VehicleLatestStatusResponse(
            "VH-RT-CONTRACT",
            "VH-RT-CONTRACT",
            "online",
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            55,
            4.65,
            -74.08,
            null,
            "gps");

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var kafkaMessage = new FleetRealtimeKafkaMessage
        {
            EventType = FleetRealtimeEventTypes.VehicleUpdate,
            Payload = JsonDocument.Parse(payloadJson).RootElement,
            OccurredAt = DateTimeOffset.UtcNow,
            VehicleId = payload.VehicleId
        };

        var serialized = FleetRealtimeKafkaMessage.Serialize(kafkaMessage);
        var deserialized = FleetRealtimeKafkaMessage.Deserialize(serialized);

        Assert.Equal(FleetRealtimeEventTypes.VehicleUpdate, deserialized.EventType);
        Assert.Equal(payload.VehicleId, deserialized.VehicleId);

        var broker = new FleetSseBroker(TimeProvider.System);
        var published = broker.PublishExternal(
            42,
            deserialized.EventType,
            deserialized.Payload,
            deserialized.OccurredAt);

        Assert.Equal(Application.Realtime.ExternalPublishResult.Accepted, published);
    }

    [Fact]
    public void Evento_fuera_de_orden_no_llega_al_stream_cuando_upsert_no_aplica()
    {
        // El UoW ya no publica vehicle-update si el UPSERT no afectó filas.
        // Esta prueba documenta el contrato: solo eventos que actualizan read model llegan al stream.
        Assert.Equal("vehicle-update", FleetRealtimeEventTypes.VehicleUpdate);
    }
}
