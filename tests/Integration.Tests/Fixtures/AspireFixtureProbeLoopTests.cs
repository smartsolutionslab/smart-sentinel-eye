using Polly.Timeout;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #3087 — a probe attempt that exceeds the client's timeout is a failed attempt, not
/// the end of the loop. Driven through a real <see cref="HttpClient"/> over a scripted
/// handler, in <c>FixtureRetryPolicyTests</c>' shape: no Docker, no Aspire fixture.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AspireFixtureProbeLoopTests
{
    private const string Path = "/probe";
    private const int ProbeAttempts = 3;

    private static readonly Uri BaseAddress = new("http://probe.test");

    [Fact]
    public async Task A_request_that_hits_the_client_timeout_counts_as_a_failed_attempt()
    {
        var server = new ScriptedHandler(Step.Hang);
        using var client = new HttpClient(server) { BaseAddress = BaseAddress, Timeout = TimeSpan.FromMilliseconds(50) };

        var succeeded = await AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            CancellationToken.None);

        succeeded.ShouldBeFalse();
        server.Attempts.ShouldBe(ProbeAttempts);
    }

    [Fact]
    public async Task A_resilience_timeout_counts_as_a_failed_attempt()
    {
        var server = new ScriptedHandler(Step.Throw(() => new TimeoutRejectedException()));
        using var client = new HttpClient(server) { BaseAddress = BaseAddress };

        var succeeded = await AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            CancellationToken.None);

        succeeded.ShouldBeFalse();
        server.Attempts.ShouldBe(ProbeAttempts);
    }

    [Fact]
    public async Task An_attempt_after_a_timed_out_one_can_still_succeed()
    {
        var server = new ScriptedHandler(Step.Hang, Step.Answer(HttpStatusCode.OK));
        using var client = new HttpClient(server) { BaseAddress = BaseAddress, Timeout = TimeSpan.FromMilliseconds(50) };

        var succeeded = await AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            CancellationToken.None);

        succeeded.ShouldBeTrue();
        server.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task The_fixtures_own_cancellation_still_propagates()
    {
        var server = new ScriptedHandler(Step.Hang);
        using var client = new HttpClient(server) { BaseAddress = BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            cancellation.Token));

        server.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_shaped_exception_under_a_cancelled_fixture_token_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var server = new ScriptedHandler(Step.Throw(() =>
        {
            cancellation.Cancel();
            return new TaskCanceledException();
        }));
        using var client = new HttpClient(server) { BaseAddress = BaseAddress };

        await Should.ThrowAsync<OperationCanceledException>(() => AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            cancellation.Token));

        server.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_connection_counts_as_a_failed_attempt()
    {
        var server = new ScriptedHandler(Step.Throw(() => new HttpRequestException()));
        using var client = new HttpClient(server) { BaseAddress = BaseAddress };

        var succeeded = await AspireFixture.PollUntilSuccessAsync(
            client,
            Path,
            ProbeAttempts,
            TimeSpan.Zero,
            CancellationToken.None);

        succeeded.ShouldBeFalse();
        server.Attempts.ShouldBe(ProbeAttempts);
    }

    private sealed record Step(Func<CancellationToken, Task<HttpResponseMessage>> Run)
    {
        public static readonly Step Hang = new(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        public static Step Throw(Func<Exception> exception) =>
            new(_ => Task.FromException<HttpResponseMessage>(exception()));

        public static Step Answer(HttpStatusCode status) =>
            new(_ => Task.FromResult(new HttpResponseMessage(status)));
    }

    /// <summary>Plays the steps in order; the last one repeats.</summary>
    private sealed class ScriptedHandler(params Step[] steps) : HttpMessageHandler
    {
        private int attempts;

        public int Attempts => attempts;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref attempts) - 1;
            return steps[Math.Min(index, steps.Length - 1)].Run(cancellationToken);
        }
    }
}
