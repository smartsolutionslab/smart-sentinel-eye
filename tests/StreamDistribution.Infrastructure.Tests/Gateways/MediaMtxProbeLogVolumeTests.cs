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

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_fab_scale_sweep_of_healthy_streams_writes_no_information_record(string environment)
    {
        const int probeCount = 250;

        (IHost host, StubHandler stub, RecordingLoggerProvider recorder) =
            BuildGatewayHost(environment, HealthyResponder);

        IRtspGateway gateway = host.Services.GetRequiredService<IRtspGateway>();

        for (int i = 0; i < probeCount; i++)
        {
            RtspPathHealth result = await gateway.GetPathHealthAsync(RandomPath(), CancellationToken.None);
            result.IsReady.ShouldBeTrue();
        }

        stub.Requests.ShouldBe(
            probeCount,
            "the stub should have seen exactly one request per probe; a different count means the harness, " +
            "not production code, produced the measurement below");

        // Service discovery is in the pipeline for real (plan §5.1 step 5) —
        // verified by inspecting the request the stub actually received, not
        // by trusting the configured string back.
        stub.RequestUris[0].ToString().StartsWith("http://mediamtx.test/v3/paths/get/", StringComparison.Ordinal)
            .ShouldBeTrue(
                $"service discovery should pass 'http://mediamtx.test' through unchanged for a host with no "
                + $"matching 'services:' configuration entry; the stub actually received '{stub.RequestUris[0]}'");

        int routine = CountRoutine(recorder.Entries);
        double perProbe = (double)routine / probeCount;
        double recordsPerSecondAt250 = perProbe * 250 / 2;

        routine.ShouldBe(
            0,
            $"{routine} routine records over {probeCount} probes = {perProbe} per probe; at 250 streams and "
            + $"a 2 s poll that is {recordsPerSecondAt250} records/s");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"ready\":false}", false)]
    [InlineData(HttpStatusCode.NotFound, "", false)]
    public async Task A_probe_answered_not_ready_or_not_found_writes_no_information_record(
        HttpStatusCode status, string body, bool expectedReady)
    {
        (IHost host, StubHandler _, RecordingLoggerProvider recorder) =
            BuildGatewayHost(Environments.Production, _ => Respond(status, body));

        IRtspGateway gateway = host.Services.GetRequiredService<IRtspGateway>();
        RtspPathHealth result = await gateway.GetPathHealthAsync(RandomPath(), CancellationToken.None);

        result.IsReady.ShouldBe(expectedReady);

        int routine = CountRoutine(recorder.Entries);

        routine.ShouldBe(
            0,
            $"{routine} routine records for a single {(int)status} probe; a '{status}' answer is a routine "
            + "exchange ('nothing changed'), not a transition, so none of it belongs at Information");
    }

    /// <summary>
    /// Characterisation (plan §5.3): must pass unmodified before and after the
    /// fix. Guards against an implementation that silences the whole
    /// <c>Polly</c> category (rejected option P2) rather than the one event
    /// the decision targets.
    /// </summary>
    [Fact]
    public async Task A_probe_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error()
    {
        (IHost host, StubHandler _, RecordingLoggerProvider recorder) = BuildGatewayHost(
            Environments.Production,
            _ => Respond(HttpStatusCode.ServiceUnavailable, string.Empty),
            zeroRetryDelay: true);

        IRtspGateway gateway = host.Services.GetRequiredService<IRtspGateway>();

        await Should.ThrowAsync<HttpRequestException>(
            () => gateway.GetPathHealthAsync(RandomPath(), CancellationToken.None));

        RecordedLog[] pollyEntries = [.. recorder.Entries.Where(entry => entry.Category == PollyCategory)];

        pollyEntries.Any(entry => entry.EffectiveEventName == ExecutionAttempt && entry.Level == LogLevel.Warning)
            .ShouldBeTrue("a handled (failed) attempt should still log at Warning");
        pollyEntries.Any(entry => entry.EffectiveEventName == OnRetry && entry.Level == LogLevel.Warning)
            .ShouldBeTrue("a retry should still log at Warning");
        pollyEntries.Any(entry => entry.EffectiveEventName == ExecutionAttempt && entry.Level == LogLevel.Error)
            .ShouldBeTrue("the final handled attempt should still log at Error");
    }

    /// <summary>
    /// Characterisation (plan §5.3): must pass unmodified before and after the
    /// fix. Guards against an implementation that silences <c>Polly</c> or
    /// <c>System.Net.Http.HttpClient</c> process-wide (rejected options P2/H3)
    /// rather than scoping to the one gateway client.
    /// </summary>
    [Fact]
    public async Task Another_client_in_the_same_service_still_logs_its_exchanges_at_information()
    {
        HostApplicationBuilder builder = NewBuilder(Environments.Production);
        RecordingLoggerProvider recorder = AttachRecorder(builder);

        // "In the same container" (AS-4) — the gateway is registered
        // alongside the unrelated client, just as production does, even
        // though this fact never calls it.
        AttachGateway(builder, HealthyResponder);

        StubHandler unrelatedStub = new(HealthyResponder);
        builder.Services.AddHttpClient("unrelated").ConfigurePrimaryHttpMessageHandler(() => unrelatedStub);

        using IHost host = builder.Build();

        IHttpClientFactory factory = host.Services.GetRequiredService<IHttpClientFactory>();
        using HttpClient client = factory.CreateClient("unrelated");
        using HttpResponseMessage response = await client.GetAsync("http://unrelated.test/ping", CancellationToken.None);

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

    /// <summary>
    /// Characterisation of the escape hatch (AS-5): red today because the
    /// Polly half cannot hold before the fix exists — the attempt is at
    /// <c>Information</c>, never <c>Debug</c>, so there is nothing for the
    /// override to bring back yet.
    /// </summary>
    [Fact]
    public async Task An_environment_override_brings_the_probe_exchange_back_without_a_rebuild()
    {
        Dictionary<string, string?> overrides = new()
        {
            ["Logging:LogLevel:" + GatewayCategoryPrefix] = "Information",
            ["Logging:LogLevel:" + PollyCategory] = "Debug",
        };

        (IHost host, StubHandler _, RecordingLoggerProvider recorder) =
            BuildGatewayHost(Environments.Production, HealthyResponder, overrides);

        IRtspGateway gateway = host.Services.GetRequiredService<IRtspGateway>();
        await gateway.GetPathHealthAsync(RandomPath(), CancellationToken.None);

        RecordedLog[] gatewayHandlerEntries =
        [
            .. recorder.Entries.Where(entry => entry.Category.StartsWith(GatewayCategoryPrefix, StringComparison.Ordinal)),
        ];

        gatewayHandlerEntries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(
            4,
            "the environment override should bring the four HttpClient handler records back at Information "
            + "without a rebuild (this half is green even before the fix — there is nothing to override yet)");

        RecordedLog attempt = recorder.Entries.Single(entry =>
            entry.Category == PollyCategory && entry.EffectiveEventName == ExecutionAttempt);

        attempt.Level.ShouldBe(
            LogLevel.Debug,
            $"an unhandled probe's ExecutionAttempt should be Debug, not {attempt.Level}, once the severity "
            + "override exists and 'Polly: Debug' lets it through — today there is no override, so this stays "
            + "at Information and the fact is red for that reason");
    }

    private static int CountRoutine(IReadOnlyList<RecordedLog> entries) =>
        entries.Count(entry =>
            entry.Level >= LogLevel.Information
            && (entry.Category.StartsWith(GatewayCategoryPrefix, StringComparison.Ordinal)
                || entry.Category == PollyCategory));

    private static MediaMtxPath RandomPath() => MediaMtxPath.From($"cam-{Guid.NewGuid()}");

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
        HostApplicationBuilder builder = NewBuilder(environment, overrides);
        RecordingLoggerProvider recorder = AttachRecorder(builder);
        (IHttpClientBuilder gatewayBuilder, StubHandler stub) = AttachGateway(builder, responder);

        if (zeroRetryDelay)
        {
            // Mirrors IdempotentRetryTests.Build: the attempt count (and, here,
            // which events fire) is what this fact is about, and that is the
            // one thing the backoff schedule does not change.
            builder.Services.Configure<HttpStandardResilienceOptions>(
                $"{gatewayBuilder.Name}-standard",
                options =>
                {
                    options.Retry.Delay = TimeSpan.Zero;
                    options.Retry.UseJitter = false;
                });
        }

        return (builder.Build(), stub, recorder);
    }

    private static HostApplicationBuilder NewBuilder(string environment, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        DirectoryInfo repoRoot = RepositoryRoot();
        string apiContentRoot = Path.Combine(repoRoot.FullName, "src", "StreamDistribution", "Api");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
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
        IHttpClientBuilder gatewayBuilder = builder.Services
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
                    foreach (object? item in enumerable)
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
