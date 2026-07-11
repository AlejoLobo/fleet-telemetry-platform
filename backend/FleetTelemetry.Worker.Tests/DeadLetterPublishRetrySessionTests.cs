using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Worker;

namespace FleetTelemetry.Worker.Tests;

public class DeadLetterPublishRetrySessionTests
{
    [Fact]
    public void First_failures_retry_without_stopping()
    {
        var session = new DeadLetterPublishRetrySession(5, 100, 1000);
        var first = session.RegisterPublishFailure(new DeadLetterPublishException("dlq"), "t", 0, 1);
        var second = session.RegisterPublishFailure(new DeadLetterPublishException("dlq"), "t", 0, 1);

        Assert.False(first.ShouldStopWorker);
        Assert.False(second.ShouldStopWorker);
        Assert.Equal(1, first.Attempt);
        Assert.Equal(2, second.Attempt);
        Assert.True(first.Delay > TimeSpan.Zero);
    }

    [Fact]
    public void Reaching_max_stops_worker_without_delay()
    {
        var session = new DeadLetterPublishRetrySession(3, 100, 1000);
        session.RegisterPublishFailure(new DeadLetterPublishException("1"), "t", 0, 1);
        session.RegisterPublishFailure(new DeadLetterPublishException("2"), "t", 0, 1);
        var last = session.RegisterPublishFailure(new DeadLetterPublishException("3"), "t", 0, 1);

        Assert.True(last.ShouldStopWorker);
        Assert.Equal(3, last.Attempt);
        Assert.Equal(TimeSpan.Zero, last.Delay);
    }
}
