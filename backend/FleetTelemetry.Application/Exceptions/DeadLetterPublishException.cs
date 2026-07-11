namespace FleetTelemetry.Application.Exceptions;

// Fallo exclusivo de publicación en el tópico dead-letter (reintento aislado en el Worker).
public sealed class DeadLetterPublishException : Exception
{
    public DeadLetterPublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DeadLetterPublishException(string message)
        : base(message)
    {
    }
}
