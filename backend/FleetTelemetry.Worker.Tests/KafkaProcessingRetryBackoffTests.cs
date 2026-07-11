using FleetTelemetry.Worker;

namespace FleetTelemetry.Worker.Tests;

public class KafkaProcessingRetryBackoffTests
{
    [Fact]
    public void First_attempt_uses_base_delay_with_jitter_up_to_25_percent()
    {
        var random = new Random(42);
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(1, 500, 5000, random);
        Assert.InRange(delay.TotalMilliseconds, 500, 625);
    }

    [Fact]
    public void Second_attempt_doubles_base_before_jitter()
    {
        var random = new Random(0);
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(2, 500, 5000, random);
        Assert.InRange(delay.TotalMilliseconds, 1000, 1250);
    }

    [Fact]
    public void High_attempt_never_exceeds_max_including_jitter()
    {
        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var delay = KafkaProcessingRetryBackoff.ComputeDelay(20, 500, 5000, random);
            Assert.True(delay.TotalMilliseconds <= 5000, $"delay={delay.TotalMilliseconds}");
        }
    }

    [Fact]
    public void Jitter_is_deterministic_with_seeded_random()
    {
        var first = KafkaProcessingRetryBackoff.ComputeDelay(3, 500, 5000, new Random(99));
        var second = KafkaProcessingRetryBackoff.ComputeDelay(3, 500, 5000, new Random(99));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Very_high_attempt_does_not_overflow()
    {
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(100, 500, 5000, new Random(1));
        Assert.True(delay.TotalMilliseconds <= 5000);
    }

    [Fact]
    public void When_initial_equals_max_jitter_is_zero()
    {
        var delay = KafkaProcessingRetryBackoff.ComputeDelay(1, 5000, 5000, new Random(1));
        Assert.Equal(5000, delay.TotalMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_attempt_throws(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KafkaProcessingRetryBackoff.ComputeDelay(attempt, 500, 5000, new Random(1)));
    }

    [Fact]
    public void Initial_greater_than_max_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KafkaProcessingRetryBackoff.ComputeDelay(1, 6000, 5000, new Random(1)));
    }
}
