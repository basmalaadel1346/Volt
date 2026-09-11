namespace Shared.Common.Exceptions;

/// <summary>
/// Thrown when a request lost a race against a concurrent one, or hit a
/// uniqueness rule the database enforces. Maps to HTTP 409 in
/// ExceptionMiddleware.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
