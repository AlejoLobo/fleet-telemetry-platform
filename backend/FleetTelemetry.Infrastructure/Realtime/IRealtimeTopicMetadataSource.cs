namespace FleetTelemetry.Infrastructure.Realtime;

// Fuente inyectable de metadata del tópico realtime (pruebas sin AdminClient real).
public interface IRealtimeTopicMetadataSource
{
    // Devuelve Partitions.Count; errores de metadata → RealtimeTopicMetadataUnavailableException.
    int GetPartitionCount(string topic, TimeSpan timeout);
}
