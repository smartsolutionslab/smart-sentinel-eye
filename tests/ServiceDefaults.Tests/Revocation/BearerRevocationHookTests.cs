using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Spec 270 (ADR-0160), plan.md §4.3's edit to
/// <c>AuthenticationDefaults.AddBearerAuthentication</c>: wire
/// <c>options.Events.OnTokenValidated</c> to refuse a token whose client is
/// listed as revoked, chaining any existing delegate first, and to survive a
/// later <c>Configure&lt;JwtBearerOptions&gt;</c> that only touches
/// <c>OnMessageReceived</c> (LayoutComposition's exact shape,
/// <c>src/LayoutComposition/Api/Program.cs:18-22</c>).
///
/// <para>
/// Built the way <c>BearerAudienceTests</c> does: the real
/// <c>AddBearerAuthentication()</c> extension method against an <em>empty</em>
/// builder, so no <c>appsettings.json</c> or ambient environment variable can
/// supply anything, and <c>IRevokedClientRegistry</c> is replaced with a stub
/// before the call so <c>TryAdd</c> (plan.md §4.3) keeps the stub rather than
/// the real registry.
/// </para>
///
/// <para>
/// <b>Red today: compile.</b> <c>IRevokedClientRegistry</c> does not exist
/// (namespace <c>SmartSentinelEye.ServiceDefaults.Revocation</c>), so nothing
/// in this file compiles until T010 lands.
/// </para>
///
/// <para>
/// <b>A judgment call this file commits to.</b> Reflection on
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer.TokenValidatedContext</c>
/// (10.0.11) shows its <c>SecurityToken</c> property is typed
/// <c>Microsoft.IdentityModel.Tokens.SecurityToken</c> at compile time; the
/// production hook (plan.md §4.3) reads <c>IssuedAt</c> off it as a
/// <c>JsonWebToken</c> because <c>AddJwtBearer</c>'s default token handler is
/// <c>JsonWebTokenHandler</c>, which populates this property with a
/// <see cref="JsonWebToken"/> at runtime. This file constructs the context
/// with an actual <see cref="JsonWebToken"/> instance so that cast succeeds.
/// </para>
///
/// <para>
/// <b>Phase-6 review, S4.</b> <see cref="CapturingRegistry"/> wraps a real
/// <see cref="RevokedClientSnapshot"/> rather than answering a fixed
/// true/false, so these tests exercise the actual §2 rule through the
/// hook's own <c>azp</c>/<c>IssuedAt</c> reads. A stub that ignores its
/// arguments cannot tell a correct read from one that always passes
/// <see langword="null"/> — every listed client would then be refused
/// forever, and the old stub-based tests would still pass.
/// </para>
/// </summary>
public class BearerRevocationHookTests
{
    private const string RevokedClientId = "kiosk-269";
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_refused_azp_fails_the_principal()
    {
        JwtBearerOptions options = BuildOptions(RegistryDisabledAt(RevokedClientId, IssuedAt));
        TokenValidatedContext context = ContextFor(options, RevokedClientId, IssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldNotBeNull();
        context.Result.Succeeded.ShouldBeFalse(
            "a registry that refuses this azp must fail the principal, so the handler emits the "
            + "standard 401 invalid_token challenge (FR-002).");
    }

    [Fact]
    public async Task An_admitted_azp_sets_no_result()
    {
        JwtBearerOptions options = BuildOptions(new CapturingRegistry(RevokedClientSnapshot.From([])));
        TokenValidatedContext context = ContextFor(options, "kiosk-never-revoked", IssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldBeNull(
            "an admitted azp must leave the context's Result untouched, so the pipeline's own "
            + "success path runs unmodified.");
    }

    /// <summary>
    /// Proves FR-001's re-registration rule at the bearer enforcement point
    /// itself (phase-6 review, S4), not only at <c>RevokedClientSnapshotTests</c>'
    /// unit level: <see cref="CapturingRegistry"/> wraps the real
    /// <see cref="RevokedClientSnapshot"/> rule, so a broken <c>azp</c>/<c>iat</c>
    /// read inside the hook (e.g. always reading <see langword="null"/>)
    /// would fail this test rather than pass it silently.
    /// </summary>
    [Fact]
    public async Task A_later_issued_token_from_a_re_registered_client_sets_no_result()
    {
        DateTimeOffset disabledAt = IssuedAt.AddMinutes(-10);
        DateTimeOffset laterIssuedAt = disabledAt.AddMinutes(6);
        CapturingRegistry registry = new(RevokedClientSnapshot.From(
            [new RevokedClientEntry(RevokedClientId, disabledAt)]));
        JwtBearerOptions options = BuildOptions(registry);
        TokenValidatedContext context = ContextFor(options, RevokedClientId, laterIssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldBeNull(
            "a client re-registered under the same id mints tokens after the old DisabledAt, and "
            + "FR-001 must admit them rather than refuse them forever for their predecessor's "
            + "revocation.");
    }

    /// <summary>
    /// Proves the hook passes the token's own <c>azp</c> and <c>iat</c> to the
    /// registry (phase-6 review, S4) — the exact silent break a stub that
    /// ignores its arguments cannot catch.
    /// </summary>
    [Fact]
    public async Task The_hook_passes_the_tokens_own_azp_and_issued_at_to_the_registry()
    {
        CapturingRegistry registry = new(RevokedClientSnapshot.From([]));
        JwtBearerOptions options = BuildOptions(registry);
        TokenValidatedContext context = ContextFor(options, RevokedClientId, IssuedAt);

        await options.Events.OnTokenValidated(context);

        registry.LastAzp.ShouldBe(RevokedClientId);
        registry.LastIssuedAt.ShouldBe(IssuedAt);
    }

    /// <summary>
    /// A pre-existing <c>OnTokenValidated</c>, registered via
    /// <c>Configure&lt;JwtBearerOptions&gt;</c> <em>before</em>
    /// <c>AddBearerAuthentication()</c> — mirroring how a hand-rolled test
    /// host composes handlers in registration order — must still run
    /// (plan.md §4.3: "chaining any existing delegate and running it first").
    /// </summary>
    [Fact]
    public async Task A_pre_existing_OnTokenValidated_configured_before_AddBearerAuthentication_still_runs()
    {
        bool previousRan = false;
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.Services.AddSingleton<IRevokedClientRegistry>(new CapturingRegistry(RevokedClientSnapshot.From([])));
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Events ??= new JwtBearerEvents();
            o.Events.OnTokenValidated = _ =>
            {
                previousRan = true;
                return Task.CompletedTask;
            };
        });

        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        JwtBearerOptions options = ResolveOptions(provider);
        TokenValidatedContext context = ContextFor(options, "kiosk-never-revoked", IssuedAt);

        await options.Events.OnTokenValidated(context);

        previousRan.ShouldBeTrue(
            "a handler already configured before AddBearerAuthentication() ran must still be "
            + "invoked; the revocation hook must chain it rather than replace it.");
    }

    /// <summary>
    /// LayoutComposition's own <c>Configure&lt;JwtBearerOptions&gt;</c>
    /// (<c>src/LayoutComposition/Api/Program.cs:18-22</c>), reproduced
    /// verbatim in shape: it runs <em>after</em> <c>AddBearerAuthentication()</c>
    /// and touches only <c>OnMessageReceived</c>, via
    /// <c>Events ??= new JwtBearerEvents()</c>. Because
    /// <c>AddBearerAuthentication()</c> must already have created a non-null
    /// <c>Events</c> object, that <c>??=</c> is a no-op and the object
    /// carrying <c>OnTokenValidated</c> survives untouched (plan.md §4.3).
    /// </summary>
    [Fact]
    public async Task A_later_configuration_that_only_sets_OnMessageReceived_leaves_OnTokenValidated_wired()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.Services.AddSingleton<IRevokedClientRegistry>(RegistryDisabledAt(RevokedClientId, IssuedAt));

        builder.AddBearerAuthentication();

        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events ??= new JwtBearerEvents();
            Func<MessageReceivedContext, Task>? existing = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                if (existing is not null)
                {
                    await existing(context);
                }
            };
        });

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        JwtBearerOptions options = ResolveOptions(provider);

        options.Events.OnTokenValidated.ShouldNotBeNull(
            "a later Configure<JwtBearerOptions> that only assigns OnMessageReceived must not "
            + "clear OnTokenValidated (plan.md §4.3).");

        TokenValidatedContext context = ContextFor(options, RevokedClientId, IssuedAt);
        await options.Events.OnTokenValidated(context);

        context.Result.ShouldNotBeNull(
            "the surviving OnTokenValidated must still be the revocation hook, not merely a "
            + "non-null delegate that does nothing.");
        context.Result.Succeeded.ShouldBeFalse();
    }

    private static CapturingRegistry RegistryDisabledAt(string clientId, DateTimeOffset issuedAt) =>
        new(RevokedClientSnapshot.From([new RevokedClientEntry(clientId, issuedAt)]));

    private static JwtBearerOptions BuildOptions(IRevokedClientRegistry registry)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.Services.AddSingleton(registry);
        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        return ResolveOptions(provider);
    }

    private static JwtBearerOptions ResolveOptions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

    private static TokenValidatedContext ContextFor(JwtBearerOptions options, string azp, DateTimeOffset issuedAt)
    {
        DefaultHttpContext httpContext = new();
        AuthenticationScheme scheme = new(
            JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));

        return new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", azp)], "Bearer")),
            SecurityToken = TokenFor(azp, issuedAt),
        };
    }

    /// <summary>
    /// An unsigned JWT carrying <c>azp</c> and <c>iat</c>, parsed into the
    /// <see cref="JsonWebToken"/> shape the production hook reads
    /// <c>IssuedAt</c> off (see the class doc's judgment-call note). Signature
    /// validation has already happened by the time <c>OnTokenValidated</c>
    /// fires, so nothing here needs to be signed.
    /// </summary>
    private static JsonWebToken TokenFor(string azp, DateTimeOffset issuedAt)
    {
        JwtSecurityToken token = new(
            claims:
            [
                new Claim("azp", azp),
                new Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat,
                    EpochTime.GetIntDate(issuedAt.UtcDateTime).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            ]);

        string encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new JsonWebToken(encoded);
    }

    /// <summary>
    /// Wraps a real <see cref="RevokedClientSnapshot"/> — the actual §2 rule —
    /// rather than answering a fixed true/false, and records the arguments it
    /// was last called with (phase-6 review, S4).
    /// </summary>
    private sealed class CapturingRegistry(RevokedClientSnapshot snapshot) : IRevokedClientRegistry
    {
        public string? LastAzp { get; private set; }

        public DateTimeOffset? LastIssuedAt { get; private set; }

        public bool Refuses(string? azp, DateTimeOffset? issuedAt)
        {
            LastAzp = azp;
            LastIssuedAt = issuedAt;
            return snapshot.Refuses(azp, issuedAt);
        }
    }
}
