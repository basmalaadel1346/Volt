namespace Shared.Common.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
