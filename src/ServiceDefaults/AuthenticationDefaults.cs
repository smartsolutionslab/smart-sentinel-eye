using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Configures JWT bearer authentication against the Keycloak realm exposed
/// by Aspire (ADR-0007 + ADR-0008 + ADR-0023). Registers one authorisation
/// policy per <see cref="Scope.All"/> entry via
/// <see cref="RequireScopeExtensions.AddScopePolicies"/>.
///
/// The Keycloak base URL is read from the Aspire-injected connection
/// string for the named keycloak resource. The realm path is appended.
/// </summary>
public static class AuthenticationDefaults
{
    public static IHostApplicationBuilder AddBearerAuthentication(
        this IHostApplicationBuilder builder,
        string keycloakResourceName = "keycloak",
        string realm = "smart-sentinel-eye")
    {
        Ensure.That(keycloakResourceName).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(realm).IsNotNull().IsNotNullOrWhiteSpace();

        var keycloakBaseUrl = KeycloakBaseUrl(builder.Configuration, keycloakResourceName);

        var authority = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}";

        AddJwtBearerScheme(builder, authority);

        AddRevocationHook(builder.Services);

        AddMetadataTransportCheck(builder);
        AddRevocationCheck(builder, keycloakBaseUrl, realm);

        builder.Services.AddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>();
        AddExceptionHandlers(builder.Services);
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthorizationBuilder()
            .AddScopePolicies(Scope.All);

        return builder;
    }

    private static string KeycloakBaseUrl(IConfiguration configuration, string keycloakResourceName)
    {
        // Aspire publishes Keycloak under one of three keys depending on
        // the dev-cert / HTTPS-upgrade configuration. Accept any of them.
        return
            configuration.GetConnectionString(keycloakResourceName)
            ?? configuration[$"services:{keycloakResourceName}:http:0"]
            ?? configuration[$"services:{keycloakResourceName}:https:0"]
            ?? throw new InvalidOperationException(
                $"Keycloak base URL not found. Looked for ConnectionStrings:{keycloakResourceName}, " +
                $"services:{keycloakResourceName}:http:0, services:{keycloakResourceName}:https:0.");
    }

    private static void AddJwtBearerScheme(IHostApplicationBuilder builder, string authority)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                // Spec 340 (#2238): strict everywhere except Development, where
                // Aspire wires Keycloak over plain http by design. The policy is
                // derived from the environment, not a literal, because a
                // configuration knob here would just rebuild the old
                // unconditional permissive default under a new name (spec 340
                // §3.4). AddMetadataTransportCheck below makes a plaintext
                // authority outside Development a startup failure instead of a
                // 500 on the first authenticated request. WhepAuthValidator
                // reads the same policy for its own retriever.
                options.RequireHttpsMetadata = RequiresHttpsMetadata(builder.Environment);
                // The audience arrives on the sse-audience client scope, which
                // every client in the realm carries as a default scope; clients
                // created at runtime get it from
                // KeycloakScopeBundles.AudienceScope, the only route open to
                // them because their representation has no mapper field.
                // Set on the collection, not on options.Audience: that one only
                // seeds the singular ValidAudience, and every later reader of
                // these options asks ValidAudiences.
                options.TokenValidationParameters.ValidAudiences = [ApiAudience];
                // Preserve original JWT claim types (`sub`, `scope`, …) instead
                // of remapping them to legacy WS-* URIs. Endpoints read `sub`
                // directly to build OperatorIdentifier.
                options.MapInboundClaims = false;
            });
    }

    private static void AddRevocationHook(IServiceCollection serviceCollection)
    {
        // Spec 270 (ADR-0160): refuse a token whose client was disabled after
        // it was minted. A separate, DI-aware Configure<JwtBearerOptions,...>
        // rather than wiring this inside AddJwtBearer's own plain
        // Action<JwtBearerOptions> above, because IRevokedClientRegistry has to
        // come from the container — every configure delegate for the same
        // named options runs in registration order against the *same*
        // JwtBearerOptions instance, so this still chains after whatever a
        // caller set before AddBearerAuthentication() ran and still survives
        // LayoutComposition's later Configure<JwtBearerOptions>, which only
        // replaces OnMessageReceived (plan.md §4.3).
        //
        // IServiceProvider, not ILoggerFactory, as the second dependency: it is
        // always resolvable, where ILoggerFactory is not on the bare
        // HostApplicationBuilder this file's own tests build (no AddLogging()),
        // so requiring it here would fail every one of them at options-resolve
        // time rather than only failing to log.
        serviceCollection.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IRevokedClientRegistry, IServiceProvider>((options, registry, services) =>
            {
                var logger = services.GetService<ILoggerFactory>()?
                    .CreateLogger("SmartSentinelEye.ServiceDefaults.AuthenticationDefaults");

                options.Events ??= new JwtBearerEvents();
                var previousOnTokenValidated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    if (previousOnTokenValidated is not null)
                    {
                        await previousOnTokenValidated(context);
                    }

                    RefuseIfRevoked(context, registry, logger);
                };
            });
    }

    private static void AddExceptionHandlers(IServiceCollection services)
    {
        // First: a request the caller got wrong is not a server failure, and
        // saying 500 sends them away to retry something that cannot succeed.
        services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
        services.AddExceptionHandler<FabAuthorizationExceptionHandler>();
        services.AddExceptionHandler<UnattributableOperatorExceptionHandler>();
        services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
        // After the concurrency handler, and the reason is worth more than the
        // rule: DbUpdateConcurrencyException derives from DbUpdateException, so
        // a handler matching the base type ahead of it would swallow every lost
        // update and report it as a name collision. UniqueConstraintExceptionHandler
        // matches the SQLSTATE instead, so it cannot — this ordering is the
        // second defence, kept because a later edit might widen that match.
        services.AddExceptionHandler<UniqueConstraintExceptionHandler>();
    }

    /// <summary>
    /// Spec 340 (#2238): outside Development, the realm's discovery document
    /// and JWKS must arrive over TLS, or an on-path attacker could substitute
    /// the signing keys a token is checked against. The flag alone is not
    /// enough — <c>JwtBearerPostConfigureOptions</c> only throws for an http
    /// authority lazily, on the first authenticated request, and nothing
    /// before this check caught a later <c>Configure&lt;JwtBearerOptions&gt;</c>
    /// re-opening <see cref="JwtBearerOptions.RequireHttpsMetadata"/>.
    /// <c>ValidateOnStart</c> moves the framework's own lazy throw to startup;
    /// the explicit <see cref="OptionsBuilder{TOptions}.Validate"/> predicate
    /// below exists only for that second case, since the framework's check
    /// already passed by the time a later Configure runs.
    /// </summary>
    private static void AddMetadataTransportCheck(IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(
                options => options.RequireHttpsMetadata || !RequiresHttpsMetadata(builder.Environment),
                $"RequireHttpsMetadata must stay true outside Development ('{builder.Environment.EnvironmentName}' "
                + "here): the realm's discovery document and JWKS must be fetched over HTTPS, or serve "
                + "Keycloak over TLS, or run under the Development environment.")
            .ValidateOnStart();
    }

    /// <summary>
    /// Spec 340 (#2238). Strict everywhere except Development — including an
    /// environment that was never set, since .NET defaults that to
    /// Production (assumption A1). Both the bearer pipeline here and
    /// <c>WhepAuthValidator</c>'s own retriever read this single definition,
    /// so the two cannot drift into asymmetric defaults.
    /// </summary>
    public static bool RequiresHttpsMetadata(IHostEnvironment environment)
    {
        Ensure.That(environment).IsNotNull();

        return !environment.IsDevelopment();
    }

    /// <summary>
    /// Spec 270 (ADR-0160): the shared registry/refresher/health-check/source
    /// every one of the nine APIs needs to enforce revocation. <c>TryAdd</c>
    /// throughout, so a caller that pre-registers <see cref="IRevokedClientRegistry"/>
    /// — a test double, or Identity's own <c>LocalRevokedClientSource</c> for
    /// <see cref="IRevokedClientSource"/> registered afterwards via
    /// <c>services.Replace</c> — wins over this default.
    /// </summary>
    private static void AddRevocationCheck(IHostApplicationBuilder builder, string keycloakBaseUrl, string realm)
    {
        // Every context's own Infrastructure module registers the system
        // TimeProvider too, later in the same composition root. TryAdd here
        // just means a bare host that calls only AddBearerAuthentication —
        // every test in this file's own test class — still has one to
        // resolve the health check and the refresher with.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<RevokedClientRegistry>();
        builder.Services.TryAddSingleton<IRevokedClientRegistry>(
            sp => sp.GetRequiredService<RevokedClientRegistry>());

        AddRevocationListOptions(builder, keycloakBaseUrl, realm);
        AddRevocationListClients(builder.Services);

        builder.Services.TryAddSingleton<RevokedClientRefresher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RevokedClientRefresher>());

        builder.Services.AddHealthChecks().AddCheck<RevokedClientSnapshotHealthCheck>(
            "revocation-snapshot",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready"]);
    }

    private static void AddRevocationListOptions(IHostApplicationBuilder builder, string keycloakBaseUrl, string realm)
    {
        // Validated only against whichever IRevokedClientSource this host
        // ends up with, resolved once the container is built — after
        // Identity's own Replace has already run (phase-6 review, S6). A
        // missing credential is then a startup failure on the eight services
        // that actually mint this token, not a silent, permanent fail-open
        // discoverable only through a Degraded health tag. Identity itself
        // replaces the source and never sets this credential, so it must not
        // be validated there — the predicate short-circuits true the moment
        // the resolved source is not HttpRevokedClientSource.
        builder.Services.AddOptions<RevocationListOptions>()
            .Configure(options =>
            {
                builder.Configuration.GetSection(RevocationListOptions.SectionName).Bind(options);
                options.KeycloakUrl = keycloakBaseUrl;
                options.Realm = realm;
            })
            .Validate<IServiceProvider>(
                (options, services) =>
                    services.GetRequiredService<IRevokedClientSource>() is not HttpRevokedClientSource
                    || (!string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret)),
                "RevocationList:ClientId and RevocationList:ClientSecret must both be set for the "
                + "HTTP revocation-list source.")
            .ValidateOnStart();
    }

    private static void AddRevocationListClients(IServiceCollection services)
    {
        // Idempotent POST (ADR-0143): a second token supersedes the first.
        services.AddHttpClient(RevocationListTokenProvider.HttpClientName).RetryEveryMethod();
        services.TryAddSingleton<RevocationListTokenProvider>();
        services.TryAddTransient<RevocationListAuthorizationHandler>();
        // A named client, not AddHttpClient<IRevokedClientSource, HttpRevokedClientSource>
        // (S3): that ties the wrapper's own lifetime to the client, which is
        // transient, while the singleton RevokedClientRefresher captures the
        // wrapper for the process lifetime. HttpRevokedClientSource instead
        // resolves this named client from the factory on every fetch.
        services.AddHttpClient(HttpRevokedClientSource.HttpClientName, client =>
        {
            // S1075 flags the literal URI, S5332 the clear-text scheme. Neither
            // applies: "identity" is a logical resource name and Aspire rewrites
            // the whole URI, scheme included (same reasoning as
            // StreamDistributionInfrastructureModule's CameraCatalogFabLookup
            // client). "https+http" — try https, fall back to http — is wrong
            // here: it resolved to Aspire's dev-mode HTTPS endpoint in CI, whose
            // self-signed certificate this HttpClient does not trust, and every
            // fetch failed with AuthenticationException: UntrustedRoot. Every
            // other inter-service client in this codebase uses plain "http" too.
            // This is a different transport than the realm metadata fetch above
            // — an inter-service call, not discovery/JWKS — and is out of scope
            // for spec 340 (#2238); it belongs with #1015's TLS story.
#pragma warning disable S1075, S5332
            client.BaseAddress = new Uri("http://identity");
#pragma warning restore S1075, S5332
        }).AddHttpMessageHandler<RevocationListAuthorizationHandler>();
        services.TryAddSingleton<IRevokedClientSource, HttpRevokedClientSource>();
    }

    /// <summary>
    /// The revocation half of FR-001/FR-002: refuses a token whose client was
    /// disabled after it was minted, taking the same 401 <c>invalid_token</c>
    /// shape any other rejected token gets.
    /// </summary>
    private static void RefuseIfRevoked(TokenValidatedContext context, IRevokedClientRegistry registry, ILogger? logger)
    {
        Ensure.That(context).IsNotNull();

        var azp = context.Principal?.FindFirst("azp")?.Value;
        var issuedAt = IssuedAtOf(context.SecurityToken);

        if (!registry.Refuses(azp, issuedAt))
        {
            return;
        }

        logger?.TokenRefusedAsRevoked(azp ?? string.Empty);
        context.Fail("client revoked");
    }

    /// <summary>
    /// <c>AddJwtBearer</c>'s default token handler is <c>JsonWebTokenHandler</c>,
    /// so <see cref="TokenValidatedContext.SecurityToken"/> is a
    /// <see cref="JsonWebToken"/> at runtime even though it is typed as the
    /// base <see cref="SecurityToken"/>. <see cref="JsonWebToken.IssuedAt"/>
    /// answers <see cref="DateTime.MinValue"/> when the token carries no
    /// <c>iat</c>, which this maps to <see langword="null"/> — the same
    /// "anomalous, and a listed client is exactly where anomalous must not
    /// pass" case <see cref="RevokedClientSnapshot.Refuses"/> already handles.
    /// </summary>
    private static DateTimeOffset? IssuedAtOf(SecurityToken? securityToken) =>
        securityToken is JsonWebToken { IssuedAt: var issuedAt } && issuedAt != DateTime.MinValue
            ? new DateTimeOffset(issuedAt, TimeSpan.Zero)
            : null;

    /// <summary>
    /// The realm client a browser kiosk authenticates as, carried in the token's
    /// <c>azp</c> claim.
    /// </summary>
    /// <remarks>
    /// Hard-coded rather than configured, and pinned by
    /// <c>KioskScopeParityTests</c> against the realm JSON — the same
    /// answer it already gives for the kiosk's scopes.
    /// A constant that cannot silently drift is cheaper here than plumbing an
    /// option through a context that needs nothing else from configuration.
    /// </remarks>
    public const string KioskClientId = "kiosk-web";

    /// <summary>
    /// The API every token in this realm is minted for, checked against the
    /// access token's <c>aud</c> claim.
    /// </summary>
    /// <remarks>
    /// The realm emits it from the <c>sse-audience</c> client scope. The literal
    /// is spelt out a second time in <c>RealmAudienceTests</c>, which reads the
    /// realm file, and a third in <c>BearerAudienceTests</c>, which reads these
    /// options — so the realm and the services cannot drift apart in silence
    /// (spec 069 FR-009).
    /// </remarks>
    public const string ApiAudience = "smart-sentinel-eye-api";
}
