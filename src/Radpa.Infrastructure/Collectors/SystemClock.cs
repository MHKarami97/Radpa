using Radpa.Application.Abstractions;

namespace Radpa.Infrastructure.Collectors;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}
