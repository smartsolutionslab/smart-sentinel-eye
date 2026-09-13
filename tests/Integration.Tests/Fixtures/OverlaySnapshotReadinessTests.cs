using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.SystemVariables;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #2201 — <c>NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync</c> maps a
/// non-200 snapshot to <see cref="string.Empty"/>, and <c>string.Empty.Contains(anything
/// non-empty)</c> answers <c>false</c> — the same answer a fully resolved label gives. So
/// the readiness check the wait exists to perform is satisfied on the very first 404: it
/// returns having waited for nothing.
///
/// <para>
/// Scripted against a hand-written <see cref="HttpMessageHandler"/> in
/// <c>FixtureRetryPolicyTests</c>' shape — a real <see cref="HttpClient"/> over a
/// scripting/counting handler, an assertion on the <b>attempt count</b>, no Docker, no
/// Aspire fixture, no <c>[Collection]</c>. <c>NFR_VariableResolutionLatencyTests</c>'
/// <c>WaitUntilResolvableAsync</c> and <c>ResolvedTextAsync</c> are widened from
/// <c>private</c> to <c>internal</c> so this file can drive today's body directly, without
/// moving the defect out of the file that has it. The widening — and this file's own
/// retargeting — is undone once both bodies move into a shared fixture (#2201's US-4).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class OverlaySnapshotReadinessTests
{
    private const string VariableName = "temperature";
    private const string ResolvedBody = "Line 1: 42";

    private static readonly Guid Overlay = Guid.NewGuid();
    private static readonly string StaleBody = $"Line 1: {{{{{VariableName}}}}}";

    /// <summary>
    /// AC-1, the primary red. Today the wait sees the first 404, maps it to
    /// <see cref="string.Empty"/>, finds no literal in it, and returns — one request. It
    /// must not return before the third, resolved, 200.
    /// </summary>
    [Fact]
    public async Task A_readiness_wait_does_not_return_while_the_snapshot_is_not_a_200()
    {
        (HttpClient client, ScriptedHandler server) = Build(
            (HttpStatusCode.NotFound, null),
            (HttpStatusCode.NotFound, null),
            (HttpStatusCode.OK, ResolvedBody));

        await NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync(
            client, Overlay, VariableName, ceilingMs: 5_000);

        server.Attempts.ShouldBe(3, "a 404, a 404, then the resolved 200 is three requests, not one.");
    }

    /// <summary>
    /// AC-2. Green today by accident — the existing condition already tests the literal —
    /// asserted so the fix is observably a narrowing of the current behaviour rather than a
    /// replacement that happens to drop the literal check.
    /// </summary>
    [Fact]
    public async Task A_readiness_wait_does_not_return_on_a_200_that_still_carries_the_literal()
    {
        (HttpClient client, ScriptedHandler server) = Build(
            (HttpStatusCode.OK, StaleBody),
            (HttpStatusCode.OK, ResolvedBody));

        await NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync(
            client, Overlay, VariableName, ceilingMs: 5_000);

        server.Attempts.ShouldBe(2, "a stale 200 must not satisfy the wait; only the resolved one does.");
    }

    /// <summary>
    /// AC-3, first half. Today this does not throw at all — a 404 forever looks identical to
    /// "resolved" because of the same empty-string defect AC-1 exercises, so the wait returns
    /// on the first request instead of ever reaching its timeout.
    /// </summary>
    [Fact]
    public async Task A_readiness_wait_that_never_sees_a_200_says_so()
    {
        (HttpClient client, _) = Build((HttpStatusCode.NotFound, null));

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync(
                client, Overlay, VariableName, ceilingMs: 500));

        exception.Message.ShouldContain(Overlay.ToString());
        exception.Message.ShouldContain(VariableName);
        exception.Message.ShouldContain("not a 200");
    }

    /// <summary>
    /// AC-3, second half. Green today: a permanently stale 200 already fails today's literal
    /// check, so the wait already throws — it just cannot yet say why. The two diagnoses must
    /// differ, so this asserts the negative half of that difference: this case's message must
    /// not claim "not a 200", which is the other case's diagnosis alone.
    /// </summary>
    [Fact]
    public async Task A_readiness_wait_that_never_resolves_quotes_the_last_text()
    {
        (HttpClient client, _) = Build((HttpStatusCode.OK, StaleBody));

        TimeoutException exception = await Should.ThrowAsync<TimeoutException>(
            () => NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync(
                client, Overlay, VariableName, ceilingMs: 500));

        exception.Message.ShouldNotContain("not a 200");
    }

    /// <summary>
    /// AC-4. Bounded on request count, never on the wall clock — a timing assertion on shared
    /// CI is the kind that flakes and then gets deleted. Today's early return leaves no loop
    /// to bound at all: it fails at the throw, never reaching the count.
    /// </summary>
    [Fact]
    public async Task A_readiness_wait_polls_rather_than_spins()
    {
        (HttpClient client, ScriptedHandler server) = Build((HttpStatusCode.NotFound, null));

        await Should.ThrowAsync<TimeoutException>(
            () => NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync(
                client, Overlay, VariableName, ceilingMs: 1_000));

        server.Attempts.ShouldBeGreaterThan(1, "one request means it returned early rather than polling.");
        server.Attempts.ShouldBeLessThan(20, "an undelayed loop against a 1 s ceiling would issue far more.");
    }

    private static (HttpClient Client, ScriptedHandler Server) Build(
        params (HttpStatusCode Status, string? Body)[] script)
    {
        ScriptedHandler server = new(script);
        HttpClient client = new(server) { BaseAddress = new Uri("https://x.test/") };

        return (client, server);
    }

    private sealed class ScriptedHandler(IReadOnlyList<(HttpStatusCode Status, string? Body)> script)
        : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (HttpStatusCode status, string? body) = script[Math.Min(Attempts, script.Count - 1)];
            Attempts++;

            HttpResponseMessage response = new(status);
            if (body is not null)
            {
                response.Content = new StringContent(
                    JsonSerializer.Serialize(new { resolvedText = body }),
                    Encoding.UTF8,
                    "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
