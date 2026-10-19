using System.Collections;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// Issue #2672 / spec 331. Pins the cost the issue measured — five
/// <c>Information</c> records per healthy MediaMTX probe, 625 records/s at
/// the 250-camera target — and the four properties the fix must not break
/// while removing it.
///
/// <para>
/// <b>The real things, bound the way the host binds them</b> (plan §5.1):
/// <see cref="Host.CreateApplicationBuilder(HostApplicationBuilderSettings)"/>
/// loads the <b>shipped</b> <c>src/StreamDistribution/Api/appsettings*.json</c>
/// files from their real <c>ContentRootPath</c>, the real
/// <c>AddServiceDefaults()</c> installs the real standard resilience handler
/// and service discovery, and <see cref="StreamDistributionInfrastructureModule.AddMediaMtxGateway"/>
/// (T001's seam) is the real gateway registration. Only the primary
/// <see cref="HttpMessageHandler"/> is a hand-written stub (ADR-0052) — no
/// mocking library, no Testcontainers (ADR-0103).
/// </para>
///
/// <para>
/// <b>Why a recording <see cref="ILoggerProvider"/> whose own
/// <see cref="ILogger.IsEnabled"/> always answers <c>true</c>:</b> the
/// filtering this spec is about happens in Microsoft.Extensions.Logging's own
/// pipeline, driven by the bound <c>Logging:LogLevel</c> configuration, before
/// a provider's logger is ever invoked (the <c>AlwaysEnabledProvider</c>
/// principle in <c>DatabaseCommandLogLevelTests</c>). A provider that filtered
/// on its own could not tell "the config let this through" from "my own
/// IsEnabled happened to agree".
/// </para>
///
/// <para>
/// <b>Why the state is inspected for an <c>EventName</c> property:</b> Polly
/// writes every non-attempt resilience event (<c>OnRetry</c>,
/// <c>OnCircuitClosed</c>, …) under one <see cref="EventId"/> — <c>0
/// "ResilienceEvent"</c> — and carries the event's own name only as a
/// structured logging property (<c>Polly.Extensions</c> 8.4.2,
/// <c>Telemetry/Log.cs:210-216</c>). <c>ExecutionAttempt</c> itself (EventId 3)
/// does not need this: its own <see cref="EventId.Name"/> already says so.
/// </para>
/// </summary>
public class MediaMtxProbeLogVolumeTests
{
    private const string GatewayCategoryPrefix = "System.Net.Http.HttpClient.IRtspGateway";
    private const string PollyCategory = "Polly";
    private const string ExecutionAttempt = "ExecutionAttempt";
    private const string OnRetry = "OnRetry";

    /// <summary>
    /// Characterisation (plan §5.3): must pass unmodified before and after the
    /// fix. Guards against an implementation that silences <c>Polly</c> or
    /// <c>System.Net.Http.HttpClient</c> process-wide (rejected options P2/H3)
    /// rather than scoping to the one gateway client.
    /// </summary>
    [Fact]
    public async Task Another_client_in_the_same_service_still_logs_its_exchanges_at_information()
    {
        var builder = NewBuilder(Environments.Production);
        var recorder = AttachRecorder(builder);

        // "In the same container" (AS-4) — the gateway is registered
        // alongside the unrelated client, just as production does, even
        // though this fact never calls it.
        AttachGateway(builder, HealthyResponder);

        StubHandler unrelatedStub = new(HealthyResponder);
        builder.Services.AddHttpClient("unrelated").ConfigurePrimaryHttpMessageHandler(() => unrelatedStub);

        using var host = builder.Build();

        var factory = host.Services.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("unrelated");
        using var response = await client.GetAsync("http://unrelated.test/ping", CancellationToken.None);

        RecordedLog[] unrelatedHandlerEntries =
        [
            .. recorder.Entries.Where(entry =>
                entry.Category.StartsWith("System.Net.Http.HttpClient.unrelated", StringComparison.Ordinal)),
        ];

        unrelatedHandlerEntries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(
            4,
            "the unrelated client's own four HttpClient handler records should still be written at "
            + "Information — this fact fails an implementation that silences System.Net.Http.HttpClient "
            + "process-wide rather than scoping to IRtspGateway");

        recorder.Entries.Any(entry =>
                entry.Category == PollyCategory
                && entry.EffectiveEventName == ExecutionAttempt
                && entry.Level == LogLevel.Information)
            .ShouldBeTrue(
                "the unrelated client's own unhandled ExecutionAttempt should still be Information — this "
                + "fact fails an implementation that silences the whole Polly category");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_fab_scale_run_of_healthy_health_sweeps_writes_no_information_record(string environment)
    {
        const int sweepCount = 250;

        (var host, var stub, var recorder) =
            BuildGatewayHost(environment, _ => ListResponder(ready: true));

        var gateway = host.Services.GetRequiredService<IRtspGateway>();

        for (var i = 0; i < sweepCount; i++)
        {
            var result = await gateway.GetAllPathsHealthAsync(CancellationToken.None);
            result.Single().Value.IsReady.ShouldBeTrue();
        }

        stub.Requests.ShouldBe(
            sweepCount,
            "the stub should have seen exactly one request per sweep; a different count means the harness, "
            + "not production code, produced the measurement below");

        stub.RequestUris[0].ToString().StartsWith("http://mediamtx.test/v3/paths/list", StringComparison.Ordinal)
            .ShouldBeTrue(
                $"the health sweep should read the list route; the stub actually received '{stub.RequestUris[0]}'");

        var routine = CountRoutine(recorder.Entries);

        routine.ShouldBe(
            0,
            $"{routine} routine records over {sweepCount} sweeps; a healthy sweep is a routine exchange, "
            + "not a transition, so none of it belongs at Information");
    }

    [Fact]
    public async Task A_health_sweep_answered_not_ready_writes_no_information_record()
    {
        (var host, var _, var recorder) =
            BuildGatewayHost(Environments.Production, _ => ListResponder(ready: false));

        var gateway = host.Services.GetRequiredService<IRtspGateway>();
        var result = await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        result.Single().Value.IsReady.ShouldBeFalse();

        var routine = CountRoutine(recorder.Entries);

        routine.ShouldBe(
            0,
            $"{routine} routine records for a single not-ready sweep; 'nothing changed' is a routine exchange");
    }

    [Fact]
    public async Task A_health_sweep_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error()
    {
        (var host, var _, var recorder) = BuildGatewayHost(
            Environments.Production,
            _ => Respond(HttpStatusCode.ServiceUnavailable, string.Empty),
            zeroRetryDelay: true);

        var gateway = host.Services.GetRequiredService<IRtspGateway>();

        await Should.ThrowAsync<HttpRequestException>(
            () => gateway.GetAllPathsHealthAsync(CancellationToken.None));

        RecordedLog[] pollyEntries = [.. recorder.Entries.Where(entry => entry.Category == PollyCategory)];

        pollyEntries.Any(entry => entry.EffectiveEventName == ExecutionAttempt && entry.Level == LogLevel.Warning)
            .ShouldBeTrue("a handled (failed) attempt should still log at Warning");
        pollyEntries.Any(entry => entry.EffectiveEventName == OnRetry && entry.Level == LogLevel.Warning)
            .ShouldBeTrue("a retry should still log at Warning");
        pollyEntries.Any(entry => entry.EffectiveEventName == ExecutionAttempt && entry.Level == LogLevel.Error)
            .ShouldBeTrue("the final handled attempt should still log at Error");
    }

    [Fact]
    public async Task An_environment_override_brings_the_health_sweep_exchange_back_without_a_rebuild()
    {
        Dictionary<string, string?> overrides = new()
        {
            ["Logging:LogLevel:" + GatewayCategoryPrefix] = "Information",
            ["Logging:LogLevel:" + PollyCategory] = "Debug",
        };

        (var host, var _, var recorder) =
            BuildGatewayHost(Environments.Production, _ => ListResponder(ready: true), overrides);

        var gateway = host.Services.GetRequiredService<IRtspGateway>();
        await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        RecordedLog[] gatewayHandlerEntries =
        [
            .. recorder.Entries.Where(entry => entry.Category.StartsWith(GatewayCategoryPrefix, StringComparison.Ordinal)),
        ];

        gatewayHandlerEntries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(
            4,
            "the environment override should bring the four HttpClient handler records back at Information "
            + "without a rebuild");

        var attempt = recorder.Entries.Single(entry =>
            entry.Category == PollyCategory && entry.EffectiveEventName == ExecutionAttempt);

        attempt.Level.ShouldBe(
            LogLevel.Debug,
            $"an unhandled sweep's ExecutionAttempt should be Debug, not {attempt.Level}, once 'Polly: Debug' lets it through");
    }

    private static HttpResponseMessage ListResponder(bool ready) =>
        Respond(
            HttpStatusCode.OK,
            "{\"pageCount\":1,\"itemCount\":1,\"items\":[{\"name\":\"cam-"
            + Guid.NewGuid()
            + "\",\"ready\":"
            + (ready ? "true" : "false")
            + "}]}");

    private static int CountRoutine(IReadOnlyList<RecordedLog> entries) =>
        entries.Count(entry =>
            entry.Level >= LogLevel.Information
            && (entry.Category.StartsWith(GatewayCategoryPrefix, StringComparison.Ordinal)
                || entry.Category == PollyCategory));

    private static HttpResponseMessage HealthyResponder(HttpRequestMessage request) =>
        Respond(HttpStatusCode.OK, "{\"ready\":true}");

    private static HttpResponseMessage Respond(HttpStatusCode status, string jsonBody) =>
        new(status)
        {
            Content = jsonBody.Length == 0 ? null : new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };

    private static (IHost Host, StubHandler Stub, RecordingLoggerProvider Recorder) BuildGatewayHost(
        string environment,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IReadOnlyDictionary<string, string?>? overrides = null,
        bool zeroRetryDelay = false)
    {
        var builder = NewBuilder(environment, overrides);
        var recorder = AttachRecorder(builder);
        (var gatewayBuilder, var stub) = AttachGateway(builder, responder);

        if (zeroRetryDelay)
        {
            // Mirrors IdempotentRetryTests.Build: the attempt count (and, here,
            // which events fire) is what this fact is about, and that is the
            // one thing the backoff schedule does not change.
            // The defaults select the pipeline per upstream authority, which
            // names its options "-standard"; the per-client name is kept too.
            foreach (var optionsName in new[] { $"{gatewayBuilder.Name}-standard", "-standard" })
            {
                builder.Services.Configure<HttpStandardResilienceOptions>(
                    optionsName,
                    options =>
                    {
                        options.Retry.Delay = TimeSpan.Zero;
                        options.Retry.UseJitter = false;
                    });
            }
        }

        return (builder.Build(), stub, recorder);
    }

    private static HostApplicationBuilder NewBuilder(string environment, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var repoRoot = RepositoryRoot();
        var apiContentRoot = Path.Combine(repoRoot.FullName, "src", "StreamDistribution", "Api");

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = apiContentRoot,
            EnvironmentName = environment,
        });

        if (overrides is not null)
        {
            // After the shipped files, the precedence position an operator's
            // environment variable occupies (plan §5.1 step 2).
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.AddServiceDefaults();

        return builder;
    }

    private static RecordingLoggerProvider AttachRecorder(HostApplicationBuilder builder)
    {
        // Clears whatever AddServiceDefaults's AddOpenTelemetry wired in, so
        // the recorder is the only provider asked — and MEL's own filter
        // rules, bound from the shipped configuration a moment ago, are the
        // only thing deciding whether it is asked at all.
        builder.Logging.ClearProviders();

        RecordingLoggerProvider recorder = new();
        builder.Logging.AddProvider(recorder);

        return recorder;
    }

    private static (IHttpClientBuilder GatewayBuilder, StubHandler Stub) AttachGateway(
        HostApplicationBuilder builder, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        builder.Services.Configure<MediaMtxOptions>(options => options.ManagementUrl = "http://mediamtx.test");

        StubHandler stub = new(responder);
        var gatewayBuilder = builder.Services
            .AddMediaMtxGateway()
            .ConfigurePrimaryHttpMessageHandler(() => stub);

        return (gatewayBuilder, stub);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    /// <summary>Hand-written primary handler (ADR-0052): counts and answers every request the same way.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly List<Uri> requestUris = [];
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        public IReadOnlyList<Uri> RequestUris
        {
            get
            {
                lock (requestUris)
                {
                    return [.. requestUris];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            lock (requestUris)
            {
                requestUris.Add(request.RequestUri ?? throw new InvalidOperationException("request had no URI"));
            }

            return Task.FromResult(responder(request));
        }
    }

    /// <summary>
    /// A provider whose own <see cref="ILogger.IsEnabled"/> always answers
    /// <c>true</c> (the <c>AlwaysEnabledProvider</c> principle) — so the only
    /// thing deciding whether a call reaches it is Microsoft.Extensions.Logging's
    /// own filtering, bound from the shipped configuration, exactly as it
    /// decides for the real sink.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<RecordedLog> entries = [];

        public IReadOnlyList<RecordedLog> Entries
        {
            get
            {
                lock (entries)
                {
                    return [.. entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

        public void Dispose()
        {
        }

        private void Record(RecordedLog entry)
        {
            lock (entries)
            {
                entries.Add(entry);
            }
        }

        private sealed class RecordingLogger(string category, RecordingLoggerProvider owner) : ILogger
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
                owner.Record(new RecordedLog(category, logLevel, eventId.Id, eventId.Name, ExtractStateEventName(state)));
            }

            /// <summary>
            /// Polly writes every non-attempt resilience event under EventId
            /// <c>0 "ResilienceEvent"</c> and carries the event's own name only
            /// as a structured property named <c>EventName</c>.
            /// </summary>
            private static string? ExtractStateEventName(object? state)
            {
                if (state is IEnumerable enumerable and not string)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is KeyValuePair<string, object> pair && pair.Key == "EventName")
                        {
                            return pair.Value?.ToString();
                        }
                    }
                }

                return null;
            }
        }
    }

    /// <summary>One recorded call to <see cref="ILogger.Log{TState}"/>.</summary>
    private sealed record RecordedLog(string Category, LogLevel Level, int EventId, string? EventIdName, string? StateEventName)
    {
        /// <summary>
        /// <see cref="StateEventName"/> when Polly carried the name as a
        /// structured property (every non-attempt event); otherwise
        /// <see cref="EventIdName"/>, which is enough for <c>ExecutionAttempt</c>
        /// itself.
        /// </summary>
        public string? EffectiveEventName => StateEventName ?? EventIdName;
    }
}
