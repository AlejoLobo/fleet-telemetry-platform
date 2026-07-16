using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

public class ConfigurationValidatorDemoEnrollmentTests
{
    [Fact]
    public void Production_rejects_AllowDemoDeviceEnrollment_true()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "true",
                ["Auth:JwtSecret"] = "integration-test-secret-with-32-chars-min",
                ["Auth:DemoPassword"] = "admin123",
                ["Auth:AllowDemoDeviceEnrollment"] = "true",
                ["Kafka:MaxProcessingAttempts"] = "3",
                ["Kafka:RetryInitialDelayMilliseconds"] = "100",
                ["Kafka:RetryMaxDelayMilliseconds"] = "1000",
                ["Kafka:RetryBackoffMultiplier"] = "2",
                ["Kafka:MaxDeadLetterPublishAttempts"] = "3",
                ["Kafka:MaxPollIntervalMilliseconds"] = "300000",
            })
            .Build();

        var environment = new StubHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(configuration, environment));

        Assert.Contains("AllowDemoDeviceEnrollment", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_allows_AllowDemoDeviceEnrollment_true()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "true",
                ["Auth:JwtSecret"] = "integration-test-secret-with-32-chars-min",
                ["Auth:DemoPassword"] = "admin123",
                ["Auth:AllowDemoDeviceEnrollment"] = "true",
                ["Kafka:MaxProcessingAttempts"] = "3",
                ["Kafka:RetryInitialDelayMilliseconds"] = "100",
                ["Kafka:RetryMaxDelayMilliseconds"] = "1000",
                ["Kafka:RetryBackoffMultiplier"] = "2",
                ["Kafka:MaxDeadLetterPublishAttempts"] = "3",
                ["Kafka:MaxPollIntervalMilliseconds"] = "300000",
            })
            .Build();

        var environment = new StubHostEnvironment { EnvironmentName = Environments.Development };

        ConfigurationValidator.Validate(configuration, environment);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "FleetTelemetry.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
