using Shared.Common.Abstractions;

namespace Shared.Common.Implementations;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
