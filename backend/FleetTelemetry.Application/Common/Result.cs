namespace FleetTelemetry.Application.Common;

public sealed record ValidationError(string Code, string Message)
{
    public static ValidationError Required(string field) =>
        new($"{field}_required", $"{field} is required.");

    public static ValidationError Range(string field, string detail) =>
        new($"{field}_invalid", detail);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Errors = Array.Empty<ValidationError>();
    }

    private Result(IReadOnlyList<ValidationError> errors)
    {
        IsSuccess = false;
        Value = default;
        Errors = errors;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(params ValidationError[] errors) =>
        new(errors);

    public static Result<T> Failure(IEnumerable<ValidationError> errors) =>
        new(errors.ToArray());

    public T GetValueOrThrow()
    {
        if (!IsSuccess)
            throw new InvalidOperationException(
                string.Join("; ", Errors.Select(error => error.Message)));

        return Value!;
    }
}
