using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

/// <summary>
/// Records the level, the <see cref="EventId.Name"/> and the rendered message of
/// everything logged, for tests that must tell two Warnings of one gateway apart
/// by the event they were logged under. <see cref="CapturingLogger{T}"/> does not
/// keep the event.
/// </summary>
internal sealed class EventNamedLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string? EventName, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string? EventName, string Message)> Entries => entries;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Ensure.That(formatter).IsNotNull();
        entries.Add((logLevel, eventId.Name, formatter(state, exception)));
    }
}
