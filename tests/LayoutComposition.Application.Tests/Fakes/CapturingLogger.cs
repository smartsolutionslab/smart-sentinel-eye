using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Records what was logged. Spec 296 FR-010 makes the log line the only
/// observable trace of a dropped rule-driven switch — "nothing changed"
/// cannot tell a diagnosable drop from a silent one, and the six drop cases
/// must not read alike (US2-8/9).
///
/// <para>
/// Mirrors <c>Automation.Application.Tests.Fakes.CapturingLogger</c> /
/// <c>SystemVariables.Application.Tests.Fakes.CapturingLogger</c>. Copied
/// rather than shared per those files' own note: test projects do not
/// reference one another.
/// </para>
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => entries;

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
        entries.Add((logLevel, formatter(state, exception), exception));
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
