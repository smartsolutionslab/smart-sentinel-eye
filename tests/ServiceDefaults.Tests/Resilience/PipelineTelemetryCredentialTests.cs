using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// The pipeline instance name is emitted by Polly as the <c>pipeline.instance</c> metric tag and in
/// its log lines. Keyed on the request authority including userinfo, a credentialed URL would write
/// its password into both (#2959).
/// </summary>
public class PipelineTelemetryCredentialTests
{
    private const string Secret = "s3cr3t-pw";

    [Fact]
    public async Task A_credential_in_the_url_reaches_neither_the_metric_tags_nor_the_log_lines()
    {
        List<string> emitted = [];
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Polly")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, _, tags, _) => Capture(emitted, tags));
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Capture(emitted, tags));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Capture(emitted, tags));
        listener.Start();

        CapturingLoggerProvider logs = new();
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.AddServiceDefaults();
        builder.Logging.AddProvider(logs);
        builder.Services.AddHttpClient("reader").ConfigurePrimaryHttpMessageHandler(() => new FailingServer());
        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
            options.CircuitBreaker.MinimumThroughput = 2;
            options.CircuitBreaker.FailureRatio = 0.5;
        });
        using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("reader");

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync($"https://reader:{Secret}@userinfo.test/thing", CancellationToken.None));
        listener.Dispose();

        emitted.ShouldNotContain(entry => entry.Contains(Secret, StringComparison.Ordinal));
        logs.Lines.ShouldNotBeEmpty("the pipeline logs its retries and the breaker opening.");
        logs.Lines.ShouldNotContain(line => line.Contains(Secret, StringComparison.Ordinal));
    }

    private static void Capture(List<string> emitted, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            lock (emitted)
            {
                emitted.Add($"{tag.Key}={tag.Value}");
            }
        }
    }

    private sealed class FailingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (lines)
                {
                    return [.. lines];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var line = formatter(state, exception);
                if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    line += " " + string.Join(' ', fields.Select(field => $"{field.Key}={field.Value}"));
                }

                lock (lines)
                {
                    lines.Add(line);
                }
            }
        }
    }
}
