using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

/// <summary>
/// Records what was logged, for the cases where the log <em>is</em> the
/// behaviour: the value-request handler fails closed on a cross-fab miss and
/// on malformed input alike, so "the variable did not change" cannot
/// distinguish a diagnosable failure from a silent one.
///
/// <para>
/// Mirrors <c>Automation.Application.Tests.Fakes.CapturingLogger</c>. Copied
/// rather than shared: test projects do not reference one another, and the
/// alternative is a shared test-support assembly that nothing else wants yet.
/// </para>
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries;

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
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add((logLevel, formatter(state, exception), exception));
    }
}

/// <summary>Outside the generic on purpose: one instance, not one per T.</summary>
internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
