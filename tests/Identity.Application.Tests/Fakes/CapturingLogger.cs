using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Keeps every entry written through it, so a test can assert on what was
/// logged — and, more to the point here, on what was <b>not</b>.
///
/// <para>
/// Hand-written rather than mocked (ADR-0054). Mirrors
/// <c>Identity.Infrastructure.Tests.Fakes.CapturingLogger</c> — #2628's
/// race-path tests are the first in this project to need a logged marker as
/// evidence rather than an untyped <c>NullLogger</c>.
/// </para>
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LoggedEntry> Entries { get; } = [];

    /// <summary>
    /// Every entry the source-generated <c>[LoggerMessage]</c> methods emitted
    /// for <paramref name="name"/> — the method name, which the generator uses
    /// as the event's name.
    /// </summary>
    public IReadOnlyList<LoggedEntry> Named(string name) =>
        Entries.Where(entry => entry.EventId.Name == name).ToArray();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    /// <summary>
    /// Always enabled: the generated log methods check this first, so a logger
    /// that answered false would record nothing and every "was not logged"
    /// assertion below would pass for the wrong reason.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LoggedEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            FieldsOf(state),
            exception));

    private static Dictionary<string, object?> FieldsOf<TState>(TState state) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? pairs
                .Where(pair => pair.Key != "{OriginalFormat}")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
}

/// <summary>One entry, as the sink would have received it.</summary>
public sealed record LoggedEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception)
{
    /// <summary>
    /// One structured field as text, or <c>null</c> when the entry does not
    /// carry it at all — which an assertion must be able to tell apart from a
    /// field carrying the wrong value.
    /// </summary>
    public string? Field(string name) =>
        Fields.TryGetValue(name, out object? value) ? value?.ToString() : null;
}
