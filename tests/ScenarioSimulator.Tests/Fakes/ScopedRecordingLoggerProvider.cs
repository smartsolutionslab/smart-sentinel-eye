using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>One record with the scopes that were open when it was written.</summary>
internal sealed record ScopedLoggedEntry(
    string Category,
    LogLevel Level,
    string Message,
    string Structured,
    string Exception,
    string Scopes)
{
    public string Text => $"[{Category}] {Level} {Message} | {Structured} | {Exception} | scopes: {Scopes}";
}

/// <summary>
/// A recorder that also reads the open scopes, the way ServiceDefaults' OpenTelemetry logger does
/// (<c>IncludeScopes = true</c>): a value in a scope is exported with every record written inside it,
/// so a secret carried by a scope is a leak even when no message names it. The recorder in
/// <see cref="RecordingLoggerProvider"/> ignores scopes, which is why this one exists.
/// </summary>
internal sealed class ScopedRecordingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly List<ScopedLoggedEntry> entries = [];

    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<ScopedLoggedEntry> Entries
    {
        get
        {
            lock (entries)
            {
                return [.. entries];
            }
        }
    }

    public string Dump => string.Join(Environment.NewLine, Entries.Select(entry => entry.Text));

    public ILogger CreateLogger(string categoryName) => new ScopedRecordingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    private void Add(ScopedLoggedEntry entry)
    {
        lock (entries)
        {
            entries.Add(entry);
        }
    }

    private sealed class ScopedRecordingLogger(ScopedRecordingLoggerProvider owner, string category) : ILogger
    {
        private static string Render(object? value) =>
            value is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : value?.ToString() ?? string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            List<string> open = [];
            owner.scopes.ForEachScope((scope, list) => list.Add(Render(scope)), open);

            owner.Add(new ScopedLoggedEntry(
                category,
                logLevel,
                formatter(state, exception),
                Render(state),
                exception?.ToString() ?? string.Empty,
                string.Join(" => ", open)));
        }
    }
}
