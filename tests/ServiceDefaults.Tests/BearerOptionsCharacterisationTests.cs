using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Characterisation (spec 369, ADR-0144), captured green on unmodified
/// <c>AddBearerAuthentication</c> before it is split. Pins the resolved
/// <see cref="JwtBearerOptions"/> the nine APIs receive, the default scheme, and
/// the edges of the revocation hook that the existing hook tests do not reach:
/// the failure message, the logger category and level, and the
/// "admitted token logs nothing" half.
///
/// <para>
/// Every expected value is a literal written here. An empty builder is used so
/// no <c>appsettings.json</c> or ambient variable can supply a value.
/// </para>
/// </summary>
public class BearerOptionsCharacterisationTests
{
    private const string RevokedClientId = "kiosk-269";
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_authority_is_the_base_url_and_the_realm()
    {
        using var provider = Compose(Environments.Production);

        BearerOptions(provider).Authority.ShouldBe("https://kc.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void Https_metadata_is_not_required_in_development()
    {
        using var provider = Compose(Environments.Development);

        BearerOptions(provider).RequireHttpsMetadata.ShouldBeFalse();
    }

    [Fact]
    public void Https_metadata_is_required_in_production()
    {
        using var provider = Compose(Environments.Production);

        BearerOptions(provider).RequireHttpsMetadata.ShouldBeTrue();
    }

    [Fact]
    public void The_valid_audiences_are_exactly_the_api_audience()
    {
        using var provider = Compose(Environments.Development);

        BearerOptions(provider).TokenValidationParameters.ValidAudiences.ShouldBe(["smart-sentinel-eye-api"]);
    }

    [Fact]
    public void The_singular_audience_is_left_unset()
    {
        using var provider = Compose(Environments.Development);

        BearerOptions(provider).Audience.ShouldBeNull();
    }

    [Fact]
    public void Inbound_claims_are_not_remapped()
    {
        using var provider = Compose(Environments.Development);

        BearerOptions(provider).MapInboundClaims.ShouldBeFalse();
    }

    [Fact]
    public void The_token_validated_event_is_wired()
    {
        using var provider = Compose(Environments.Development);

        BearerOptions(provider).Events.OnTokenValidated.ShouldNotBeNull();
    }

    [Fact]
    public void The_default_authentication_scheme_is_bearer()
    {
        using var provider = Compose(Environments.Development);

        provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme.ShouldBe("Bearer");
    }

    [Fact]
    public async Task The_bearer_scheme_is_handled_by_the_jwt_bearer_handler()
    {
        using var provider = Compose(Environments.Development);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync("Bearer");

        scheme.ShouldNotBeNull();
        scheme.HandlerType.ShouldBe(typeof(JwtBearerHandler));
    }

    [Fact]
    public async Task A_refused_token_fails_with_the_client_revoked_message()
    {
        using var provider = Compose(Environments.Development, registry: RegistryDisabledAt(RevokedClientId, IssuedAt));
        var options = BearerOptions(provider);
        var context = ContextFor(options, RevokedClientId, IssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldNotBeNull();
        context.Result.Failure.ShouldNotBeNull();
        context.Result.Failure.Message.ShouldBe("client revoked");
    }

    [Fact]
    public async Task A_refused_token_is_logged_once_at_debug_under_the_authentication_defaults_category()
    {
        RecordingProvider recorder = new();
        using var provider = Compose(
            Environments.Development,
            registry: RegistryDisabledAt(RevokedClientId, IssuedAt),
            recorder: recorder);
        var options = BearerOptions(provider);
        var context = ContextFor(options, RevokedClientId, IssuedAt);

        await options.Events.OnTokenValidated(context);

        var record = recorder.Records.ShouldHaveSingleItem();
        record.Category.ShouldBe("SmartSentinelEye.ServiceDefaults.AuthenticationDefaults");
        record.Level.ShouldBe(LogLevel.Debug);
        record.Message.ShouldBe(
            "Refused a token for 'kiosk-269': its client was disabled after the token was minted (ADR-0160).");
    }

    [Fact]
    public async Task An_admitted_token_is_not_logged()
    {
        RecordingProvider recorder = new();
        using var provider = Compose(
            Environments.Development,
            registry: new StubRegistry(RevokedClientSnapshot.From([])),
            recorder: recorder);
        var options = BearerOptions(provider);
        var context = ContextFor(options, "kiosk-never-revoked", IssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldBeNull();
        recorder.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_token_without_a_logger_factory_is_still_refused()
    {
        using var provider = Compose(Environments.Development, registry: RegistryDisabledAt(RevokedClientId, IssuedAt));
        var options = BearerOptions(provider);
        var context = ContextFor(options, RevokedClientId, IssuedAt);

        await options.Events.OnTokenValidated(context);

        context.Result.ShouldNotBeNull();
        context.Result.Succeeded.ShouldBeFalse();
    }

    private static ServiceProvider Compose(
        string environment,
        IRevokedClientRegistry? registry = null,
        RecordingProvider? recorder = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
        });
        builder.Configuration["ConnectionStrings:keycloak"] = "https://kc.invalid";

        if (registry is not null)
        {
            builder.Services.AddSingleton(registry);
        }

        if (recorder is not null)
        {
            builder.Services.AddSingleton<ILoggerFactory>(
                new LoggerFactory([recorder], new LoggerFilterOptions { MinLevel = LogLevel.Trace }));
        }

        builder.AddBearerAuthentication();

        return builder.Services.BuildServiceProvider();
    }

    private static JwtBearerOptions BearerOptions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

    private static StubRegistry RegistryDisabledAt(string clientId, DateTimeOffset disabledAt) =>
        new(RevokedClientSnapshot.From([new RevokedClientEntry(clientId, disabledAt)]));

    private static TokenValidatedContext ContextFor(JwtBearerOptions options, string azp, DateTimeOffset issuedAt)
    {
        DefaultHttpContext httpContext = new();
        AuthenticationScheme scheme = new("Bearer", "Bearer", typeof(JwtBearerHandler));

        return new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", azp)], "Bearer")),
            SecurityToken = TokenFor(azp, issuedAt),
        };
    }

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

        return new JsonWebToken(new JwtSecurityTokenHandler().WriteToken(token));
    }

    private sealed class StubRegistry(RevokedClientSnapshot snapshot) : IRevokedClientRegistry
    {
        public bool Refuses(string? azp, DateTimeOffset? issuedAt) => snapshot.Refuses(azp, issuedAt);
    }

    private sealed record LogRecord(string Category, LogLevel Level, string Message);

    private sealed class RecordingProvider : ILoggerProvider
    {
        private readonly List<LogRecord> records = [];

        public IReadOnlyList<LogRecord> Records => records;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, records);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(string category, List<LogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Add(new LogRecord(category, logLevel, formatter(state, exception)));
    }
}
