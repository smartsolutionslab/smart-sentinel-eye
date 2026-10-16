using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// One log record as a leak would reach a reader: the rendered message, the structured
/// values it was built from, and the exception text. A secret moved out of the message
/// and into a field or an exception is still a leak, so <see cref="Text"/> joins all three.
/// </summary>
internal sealed record LoggedEntry(string Category, LogLevel Level, string Message, string Structured, string Exception)
{
    public string Text => $"[{Category}] {Message} | {Structured} | {Exception}";
}

/// <summary>
/// Keeps every record of every category it is attached to (ADR-0054, hand-written). It is
/// both an <see cref="ILoggerProvider"/>, so framework categories such as IHttpClientFactory's
/// reach it through the real logging configuration, and a source of typed loggers for tests
/// that construct a class directly.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<LoggedEntry> entries = [];

    private readonly ILoggerFactory factory;

    public RecordingLoggerProvider() =>
        factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(this));

    public IReadOnlyList<LoggedEntry> Entries
    {
        get
        {
            lock (entries)
            {
                return [.. entries];
            }
        }
    }

    /// <summary>Every record, one per line, so a failed assertion prints what was logged.</summary>
    public string Dump => string.Join(Environment.NewLine, Entries.Select(entry => entry.Text));

    public ILogger<T> CreateLogger<T>() => factory.CreateLogger<T>();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(LoggedEntry entry)
    {
        lock (entries)
        {
            entries.Add(entry);
        }
    }

    private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;

            owner.Add(new LoggedEntry(
                category,
                logLevel,
                formatter(state, exception),
                structured,
                exception?.ToString() ?? string.Empty));
        }
    }
}
