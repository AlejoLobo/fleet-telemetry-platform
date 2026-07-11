using System.Text.Json;
using Confluent.Kafka;

namespace FleetTelemetry.Integration.Tests;

// Utilidades para consumir y validar mensajes del tópico DLQ real.
internal static class KafkaDlqTestHelper
{
    public static ConsumeResult<string, string>? ConsumeOne(
        string bootstrapServers,
        string topic,
        string groupId,
        TimeSpan timeout)
    {
        using var consumer = CreateConsumer(bootstrapServers, topic, groupId);
        try
        {
            return consumer.Consume(timeout);
        }
        catch (ConsumeException)
        {
            return null;
        }
    }

    public static async Task<ConsumeResult<string, string>> WaitUntilDlqMessageAsync(
        string bootstrapServers,
        string dlqTopic,
        TimeSpan timeout)
    {
        var groupId = $"dlq-reader-{Guid.NewGuid():N}";
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var result = ConsumeOne(bootstrapServers, dlqTopic, groupId, TimeSpan.FromSeconds(2));
            if (result?.Message?.Value is not null)
                return result;

            await Task.Delay(100);
        }

        throw new TimeoutException($"No se recibió mensaje en DLQ '{dlqTopic}' dentro de {timeout}.");
    }

    public static JsonElement ParseDlqPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IConsumer<string, string> CreateConsumer(
        string bootstrapServers,
        string topic,
        string groupId)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = true,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

        consumer.Subscribe(topic);
        return consumer;
    }
}
