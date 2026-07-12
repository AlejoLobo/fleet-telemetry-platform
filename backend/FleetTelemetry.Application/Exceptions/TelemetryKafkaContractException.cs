namespace FleetTelemetry.Application.Exceptions;

// Error terminal de contrato Kafka (no reintentar como fallo transitorio).
public sealed class TelemetryKafkaContractException : Exception
{
    public string ErrorCode { get; }

    public TelemetryKafkaContractException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
