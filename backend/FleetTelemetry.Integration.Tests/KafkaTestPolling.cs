using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace FleetTelemetry.Integration.Tests;

// Utilidades de polling observables para pruebas de integración Kafka.
internal static class KafkaTestPolling
{
    public static async Task WaitUntilConsumerAssignedAsync(
        string bootstrapServers,
        string groupId,
        string topic,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var admin = CreateAdmin(bootstrapServers);
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsConsumerAssignedAsync(admin, groupId, topic))
                return;

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException(
            $"Consumer group '{groupId}' was not assigned to topic '{topic}' within {timeout}.");
    }

    public static async Task WaitUntilConsumerGroupEmptyAsync(
        string bootstrapServers,
        string groupId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var admin = CreateAdmin(bootstrapServers);
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsConsumerGroupEmptyAsync(admin, groupId))
                return;

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException(
            $"Consumer group '{groupId}' still had active members after {timeout}.");
    }

    private static IAdminClient CreateAdmin(string bootstrapServers) =>
        new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            SocketTimeoutMs = 5_000
        }).Build();

    private static async Task<bool> IsConsumerAssignedAsync(
        IAdminClient admin,
        string groupId,
        string topic)
    {
        try
        {
            var descriptions = await admin.DescribeConsumerGroupsAsync([groupId]);
            var group = descriptions.ConsumerGroupDescriptions.FirstOrDefault();
            if (group is null || group.Members.Count == 0)
                return false;

            if (group.Members.Any(member =>
                    member.Assignment.TopicPartitions.Any(partition => partition.Topic == topic)))
            {
                return true;
            }

            // Algunos brokers exponen el grupo activo antes de la asignación explícita de particiones.
            return group.State is ConsumerGroupState.Stable or ConsumerGroupState.PreparingRebalance;
        }
        catch (KafkaException)
        {
            return false;
        }
    }

    private static async Task<bool> IsConsumerGroupEmptyAsync(IAdminClient admin, string groupId)
    {
        try
        {
            var descriptions = await admin.DescribeConsumerGroupsAsync([groupId]);
            var group = descriptions.ConsumerGroupDescriptions.FirstOrDefault();
            if (group is null)
                return true;

            return group.Members.Count == 0
                || group.State == ConsumerGroupState.Dead
                || group.State == ConsumerGroupState.Empty;
        }
        catch (KafkaException ex) when (ex.Error.Code == ErrorCode.GroupIdNotFound)
        {
            return true;
        }
        catch (KafkaException)
        {
            return false;
        }
    }
}
