using FleetTelemetry.Worker;

namespace FleetTelemetry.Worker.Tests;

public class KafkaProcessingRetryBackoffTests
{
    [Fact]
    public void First_attempt_uses_initial_delay_plus_jitter_within_bound()
    {
        var random = new Random(42);
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(1, 500, 5000, random);

        Assert.InRange(delay.TotalMilliseconds, 500, 500 + 500 / 4);
    }

    [Fact]
    public void Exponential_growth_is_capped_at_max_delay_plus_jitter()
    {
        var random = new Random(7);
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(10, 500, 5000, random);

        Assert.InRange(delay.TotalMilliseconds, 5000, 5000 + 5000 / 4);
    }

    [Fact]
    public void Second_attempt_doubles_base_before_jitter()
    {
        var random = new Random(0);
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(2, 500, 5000, random);

        Assert.InRange(delay.TotalMilliseconds, 1000, 1000 + 1000 / 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_attempt_throws(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KafkaProcessingRetryBackoff.ComputeDelay(attempt, 500, 5000, new Random(1)));
    }
}
