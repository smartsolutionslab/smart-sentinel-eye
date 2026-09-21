using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Spec 119 (issue #2160) — a realm that cannot be reached is a refusal, not a
/// crash.
///
/// <para>
/// <b>What is broken.</b> <c>ValidateAsync</c> awaits
/// <c>GetConfigurationAsync</c> directly. <c>ConfigurationManager</c> wraps
/// whatever the retriever threw in <c>InvalidOperationException</c> (IDX20803),
/// which is neither <c>SecurityTokenException</c> nor <c>ArgumentException</c>,
/// so it escapes both catches, escapes the handler, and escapes the endpoint —
/// MediaMTX gets a 500 while every other refusal on this hook is a typed
/// <c>ApiError</c>.
/// </para>
///
/// <para>
/// <b>No Docker, no network, no realm</b> — the metadata seam from #2099. The
/// retriever here throws the same <c>IOException</c> shape
/// <c>HttpDocumentRetriever</c> throws on a refused connection, so the exception
/// the validator meets is the production one, built by the production
/// <c>ConfigurationManager</c>.
/// </para>
///
/// <para>
/// <b>Spec 170 (issue #2418).</b> <c>ConfigurationManager&lt;T&gt;</c> 8.19.2
/// fetches metadata with <c>CancellationToken.None</c> on both its code
/// paths — a documented design choice in the library's own source, not an
/// oversight — so no test in this file may wait for a caller's token to
/// interrupt a fetch already under way; a fake that only completes when its
/// token is cancelled hangs the host instead. The one guarantee that exists is
/// the already-cancelled check on <c>ConfigurationManager</c>'s configuration
/// lock — <c>SemaphoreSlim.WaitAsync(cancel)</c> — which honours a token both
/// on entry and while queued behind another caller's in-flight first fetch:
/// see <see cref="A_request_cancelled_before_the_realm_is_reached_stays_cancelled"/>
/// for the former and
/// <see cref="A_viewer_queued_behind_another_viewers_first_fetch_can_still_cancel"/>
/// for the latter. Nowhere else does <c>GetConfigurationAsync</c> read this
/// token before a configuration is cached. A metadata fetch that cannot
/// complete — cancellation reaching the retriever included — already reports
/// as <c>IdentityProviderUnavailable</c>, which is correct: an unreachable
/// realm is an unreachable realm regardless of why the caller stopped
/// waiting.
/// </para>
/// </summary>
public sealed class WhepValidatorUnreachableRealmTests : IDisposable
{
    private const string Authority = "https://keycloak.invalid/realms/smart-sentinel-eye";

    private const string JwksUri = Authority + "/protocol/openid-connect/certs";

    private const string SigningKeyIdentifier = "whep-validator-unreachable-tests";

    private readonly RSA signingKey = RSA.Create(2048);

    private readonly CapturingLogger<WhepAuthValidator> logs = new();

    public void Dispose() => signingKey.Dispose();

    /// <summary>
    /// <b>The red.</b> Today this does not fail an assertion — it throws
    /// IDX20803 out of <c>ValidateAsync</c>, which is the defect itself.
    /// </summary>
    [Fact]
    public async Task An_unreachable_realm_refuses_instead_of_throwing()
    {
        WhepAuthValidator validator = ValidatorOver(new UnreachableRealm());

        Result<WhepAuthSubject, WhepAuthFailure> outcome =
            await validator.ValidateAsync(AToken(), CancellationToken.None);

        outcome.IsFailure.ShouldBeTrue(
            customMessage: "a token was authorized while the realm that vouches for it could not be "
            + "reached. Nothing may be admitted on a token nothing checked (spec 119 FR-001).");
        outcome.Error.ShouldBe(
            WhepAuthFailure.IdentityProviderUnavailable,
            customMessage: "an unreachable realm was reported as a rejected token, so the hook tells "
            + "MediaMTX the credential is bad while Keycloak is the thing that is down "
            + "(spec 119 FR-002).");
    }

    /// <summary>
    /// The outage is swallowed into a refusal, so the exception is the only thing
    /// left that says <em>why</em> the realm was unreachable. A swallowed
    /// exception with no trace is a review blocker; this is the assertion that
    /// keeps it from becoming one (FR-005).
    /// </summary>
    [Fact]
    public async Task An_unreachable_realm_is_logged_once_with_the_exception()
    {
        WhepAuthValidator validator = ValidatorOver(new UnreachableRealm());

        await validator.ValidateAsync(AToken(), CancellationToken.None);

        (LogLevel Level, string Message, Exception? Exception) entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldNotBeNull().Message.ShouldContain(
            "IDX20803",
            customMessage: "the log records that the realm was unreachable but not why. IDX20803 "
            + "names the address and wraps the transport failure — DNS, refused, TLS — and nothing "
            + "else survives the refusal (spec 119 FR-005).");
    }

    /// <summary>
    /// <b>Cancellation stays cancellation — for the guarantee that actually
    /// exists.</b> A token cancelled outright before <c>ValidateAsync</c> is ever
    /// called must leave as a cancellation, never as a refused viewer.
    /// <c>UnreachableRealm</c>, not a delaying fake, and no timer anywhere: with
    /// a live token this exact arrangement is
    /// <c>An_unreachable_realm_refuses_instead_of_throwing</c>, which asserts
    /// <c>IdentityProviderUnavailable</c>. Same fake, same validator, only the
    /// token differs — so the outcome is pinned to the token and nothing else,
    /// no delay, no scheduler, no window to widen.
    /// </summary>
    /// <remarks>
    /// What protects it is the narrowness of the catch in <c>ValidateAsync</c>:
    /// with <c>catch (Exception)</c> in place of
    /// <c>catch (InvalidOperationException)</c> the cancellation is swallowed
    /// into that same refusal, and this test fails on exactly that edit — the
    /// protection spec 119 built.
    /// </remarks>
    [Fact]
    public async Task A_request_cancelled_before_the_realm_is_reached_stays_cancelled()
    {
        WhepAuthValidator validator = ValidatorOver(new UnreachableRealm());
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => validator.ValidateAsync(AToken(), cancelled.Token),
            customMessage: "a token cancelled before the realm was ever reached must leave as a "
            + "cancellation, not as an IdentityProviderUnavailable refusal — widening the catch in "
            + "ValidateAsync converts this into exactly that refusal instead.");

        logs.Entries.ShouldBeEmpty(
            customMessage: "a caller that went away before the realm was reached wrote a log entry. "
            + "The transition logger only fires when the widened catch turns this into a refusal — "
            + "an entry here means that regression, not this one.");
    }

    /// <summary>
    /// <b>The boundary the library actually draws, characterised.</b>
    /// <c>ConfigurationManager&lt;T&gt;</c> 8.19.2 fetches the retriever with
    /// <c>CancellationToken.None</c> on both its code paths — a cancellation
    /// that arrives once the first metadata read is already in flight does not
    /// abandon it. This is the tripwire whose absence made #2418 possible: if a
    /// future library version starts threading the caller's token into the
    /// retriever, this test goes red, and red here is the warning that
    /// <see cref="A_request_cancelled_before_the_realm_is_reached_stays_cancelled"/>'s
    /// neighbouring assumption has moved, not a regression to chase.
    /// </summary>
    [Fact]
    public async Task A_cancellation_arriving_mid_fetch_does_not_abandon_the_first_metadata_read()
    {
        SlowRealm realm = new(DiscoveryDocument, JsonWebKeySet());
        WhepAuthValidator validator = ValidatorOver(realm);
        using CancellationTokenSource cancelled = new();

        Task<Result<WhepAuthSubject, WhepAuthFailure>> validating =
            validator.ValidateAsync(AToken(), cancelled.Token);
        await realm.Entered;
        await cancelled.CancelAsync();

        Result<WhepAuthSubject, WhepAuthFailure> outcome = await validating;

        outcome.IsSuccess.ShouldBeTrue(
            customMessage: "the first metadata read was abandoned by a cancellation that arrived "
            + "after it was already under way — ConfigurationManager 8.19.2 fetches with "
            + "CancellationToken.None by design, so this can only mean the library changed its mind.");
    }

    /// <summary>
    /// <b>The one in-flight cancellation the library genuinely honours.</b>
    /// <c>WhepAuthValidator</c> is a singleton and a wall of kiosks opens WHEP at
    /// once, so a viewer queued behind another viewer's first fetch is the
    /// normal state during a cold-cache outage, not an exotic one. Deterministic
    /// by construction, not by luck: awaiting <see cref="GatedRealm.Entered"/>
    /// before starting the second viewer guarantees caller A already holds the
    /// configuration lock and is blocked on the retriever, so caller B's
    /// <c>SemaphoreSlim.WaitAsync</c> is certain to queue rather than race —
    /// there is only the one path to <c>OperationCanceledException</c> here,
    /// not two whose order might vary. Caller B can never reach the retriever,
    /// because caller A holds the lock.
    /// </summary>
    [Fact]
    public async Task A_viewer_queued_behind_another_viewers_first_fetch_can_still_cancel()
    {
        GatedRealm realm = new(DiscoveryDocument, JsonWebKeySet());
        WhepAuthValidator validator = ValidatorOver(realm);
        using CancellationTokenSource cancelledForSecondViewer = new();

        Task<Result<WhepAuthSubject, WhepAuthFailure>> firstViewer =
            validator.ValidateAsync(AToken(), CancellationToken.None);
        await realm.Entered;

        Task<Result<WhepAuthSubject, WhepAuthFailure>> secondViewer =
            validator.ValidateAsync(AToken(), cancelledForSecondViewer.Token);
        await cancelledForSecondViewer.CancelAsync();

        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => secondViewer);
        }
        finally
        {
            // Release even if the assertion above throws — otherwise a failing
            // assertion here strands caller A on GatedRealm's 5 s cap instead of
            // completing it immediately.
            realm.Release();
        }

        Result<WhepAuthSubject, WhepAuthFailure> firstOutcome = await firstViewer;
        firstOutcome.IsSuccess.ShouldBeTrue(
            customMessage: "the second viewer's cancellation must not affect the first viewer's "
            + "own in-flight fetch.");
    }

    /// <summary>
    /// The control: the identical harness with a realm that answers accepts the
    /// identical token. Without it, the refusal above could be bought by a
    /// broken token, a stale <c>exp</c> or a mismatched issuer rather than by
    /// the outage.
    /// </summary>
    [Fact]
    public async Task A_reachable_realm_still_authorizes_the_same_token()
    {
        WhepAuthValidator validator = ValidatorOver(new ReachableRealm(DiscoveryDocument, JsonWebKeySet()));

        Result<WhepAuthSubject, WhepAuthFailure> outcome =
            await validator.ValidateAsync(AToken(), CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue(
            customMessage: "the harness itself refuses this token, so the outage cases above prove "
            + "nothing about the outage.");
    }


    /// <summary>
    /// <b>One line per outage, not one per refused viewer.</b>
    /// Spec 208 (#2284) put a per-source ceiling on <c>/streams/authorize</c>,
    /// but that bounds one anonymous caller's own rate — it does not bound how
    /// many admitted sources hit this outage catch at once, and a realm outage
    /// is exactly the moment every concurrent WHEP open across the fab (spec
    /// 208's ≈100 sessions) reaches it together. A wall of kiosks retrying
    /// through a cold-cache outage would still write a Warning and a full
    /// exception chain per request into the single OTLP sink — drowning the
    /// very diagnosis this change exists to provide (phase 6).
    /// </summary>
    [Fact]
    public async Task An_outage_is_logged_once_however_many_viewers_are_refused()
    {
        ScriptedRealm realm = new(SigningKey()) { Reachable = false };
        WhepAuthValidator validator = ValidatorOver(realm);

        for (int viewer = 0; viewer < 25; viewer++)
        {
            await validator.ValidateAsync(AToken(), CancellationToken.None);
        }

        logs.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(
            1,
            customMessage: "the outage was logged once per refused viewer. Twenty-five WHEP opens "
            + "wrote twenty-five stack traces, and a fab has 250 kiosks (spec 119, phase 6).");
    }

    /// <summary>
    /// The transition is a transition, not a latch: an outage that ends and
    /// returns is two outages, and the second one has to say so. A flag that was
    /// only ever set would make the second outage silent — the worse failure of
    /// the two, because by then the operator has seen the recovery.
    /// </summary>
    [Fact]
    public async Task A_recovery_is_logged_and_a_second_outage_speaks_again()
    {
        ScriptedRealm realm = new(SigningKey()) { Reachable = false };
        WhepAuthValidator validator = ValidatorOver(realm);

        await validator.ValidateAsync(AToken(), CancellationToken.None);
        realm.Reachable = true;
        await validator.ValidateAsync(AToken(), CancellationToken.None);
        realm.Reachable = false;
        await validator.ValidateAsync(AToken(), CancellationToken.None);

        logs.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(
            2,
            customMessage: "the second outage was swallowed. The flag latched instead of tracking "
            + "the transition, so an operator who saw the recovery is told nothing when it breaks "
            + "again (spec 119, phase 6).");
        logs.Entries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(
            1,
            customMessage: "the recovery was not logged, so the outage warning has no closing "
            + "bracket and an operator cannot tell a resolved outage from an ongoing one.");
    }

    private WhepAuthValidator ValidatorOver(IConfigurationManager<OpenIdConnectConfiguration> metadata) =>
        new(metadata, logs);

    private WhepAuthValidator ValidatorOver(IDocumentRetriever retriever) =>
        new(
            new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{Authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                retriever),
            logs);

    private static string DiscoveryDocument =>
        $$"""{"issuer":"{{Authority}}","jwks_uri":"{{JwksUri}}"}""";

    private string JsonWebKeySet()
    {
        RSAParameters publicKey = signingKey.ExportParameters(includePrivateParameters: false);
        string modulus = Base64UrlEncoder.Encode(publicKey.Modulus!);
        string exponent = Base64UrlEncoder.Encode(publicKey.Exponent!);

        return $$"""
            {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{SigningKeyIdentifier}}","n":"{{modulus}}","e":"{{exponent}}"}]}
            """;
    }

    private RsaSecurityKey SigningKey() =>
        new(signingKey) { KeyId = SigningKeyIdentifier };

    private string AToken()
    {
        SigningCredentials credentials = new(SigningKey(), SecurityAlgorithms.RsaSha256);

        JwtSecurityToken token = new(
            issuer: Authority,
            audience: AuthenticationDefaults.ApiAudience,
            claims: [new Claim("sub", "kiosk-operator"), new Claim("scope", "sse.streams.read")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// The shape <c>HttpDocumentRetriever</c> throws when the socket is refused:
    /// IDX20804 wrapping the transport failure. <c>ConfigurationManager</c> turns
    /// it into IDX20803, exactly as it does in a fab whose Keycloak is restarting.
    /// </summary>
    private sealed class UnreachableRealm : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
            throw new IOException(
                $"IDX20804: Unable to retrieve document from: '{address}'.",
                new HttpRequestException("No connection could be made because the target machine actively refused it."));
    }

    /// <summary>
    /// A realm that answers after a short, fixed bound — long enough that a
    /// caller cancelling once the first fetch is under way finds it still in
    /// flight, short enough that no token this fake is handed can make it
    /// outlive that bound. <paramref name="cancel"/> <b>is</b> threaded into
    /// the delay, deliberately: today's <c>ConfigurationManager&lt;T&gt;</c>
    /// 8.19.2 always calls this retriever with <c>CancellationToken.None</c>
    /// (measured against the library's own source, not assumed), so the token
    /// this fake receives is never actually cancelled and the fixed delay is
    /// what gates it in practice — but if a future library version starts
    /// forwarding the caller's real token here instead, that token *will* be
    /// cancelled mid-delay, this call throws
    /// <see cref="OperationCanceledException"/>, and
    /// <see cref="A_cancellation_arriving_mid_fetch_does_not_abandon_the_first_metadata_read"/>
    /// goes red — the tripwire the test's own doc comment claims. A fake that
    /// discarded <paramref name="cancel"/> outright could never detect that
    /// drift; the bound alone, not the token, is what keeps this fake from
    /// hanging regardless of which library behaviour is in effect.
    /// </summary>
    private sealed class SlowRealm(string discovery, string keys) : IDocumentRetriever
    {
        private static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(200);

        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int fetches;

        public Task Entered => entered.Task;

        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            if (Interlocked.Exchange(ref fetches, 1) == 0)
            {
                entered.TrySetResult();
                await Task.Delay(Bound, cancel);
            }

            return address == JwksUri ? keys : discovery;
        }
    }

    /// <summary>
    /// A metadata source whose reachability is scripted. Used where the real
    /// <see cref="ConfigurationManager{T}"/> cannot be: it caches a document once
    /// obtained, so an outage <em>following</em> a success never reaches the
    /// validator through it — and the transition back is exactly what these two
    /// tests are about. The failure is the exception ConfigurationManager raises,
    /// IDX20803 and all.
    /// </summary>
    private sealed class ScriptedRealm(SecurityKey signingKey) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public bool Reachable { get; set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            if (!Reachable)
            {
                throw new InvalidOperationException(
                    $"IDX20803: Unable to obtain configuration from: '{Authority}/.well-known/openid-configuration'.",
                    new IOException("IDX20804: Unable to retrieve document."));
            }

            OpenIdConnectConfiguration configuration = new() { Issuer = Authority };
            configuration.SigningKeys.Add(signingKey);

            return Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
        }
    }

    private sealed class ReachableRealm(string discovery, string keys) : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
            Task.FromResult(address == JwksUri ? keys : discovery);
    }

    /// <summary>
    /// Holds the first caller inside the retriever until released, so a second
    /// caller queued behind it — behind <c>ConfigurationManager</c>'s
    /// configuration lock, not behind this fake — can be cancelled while
    /// genuinely waiting. The hold is itself bounded by
    /// <c>Task.WhenAny</c> against a hard cap, never a bare await on the gate:
    /// a test failure that forgets to call <see cref="Release"/> must not
    /// become a hung host.
    /// </summary>
    private sealed class GatedRealm(string discovery, string keys) : IDocumentRetriever
    {
        private static readonly TimeSpan HardCap = TimeSpan.FromSeconds(5);

        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int fetches;

        public Task Entered => entered.Task;

        public void Release() => gate.TrySetResult();

        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            if (Interlocked.Exchange(ref fetches, 1) == 0)
            {
                entered.TrySetResult();
                await Task.WhenAny(gate.Task, Task.Delay(HardCap, CancellationToken.None));
            }

            return address == JwksUri ? keys : discovery;
        }
    }
}
