using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 069 — a token names the API it is for.
///
/// <para>
/// <b>What is broken.</b> <c>AddBearerAuthentication</c> turns audience
/// validation off, so every one of the nine APIs accepts any signature-valid
/// token this realm issues, whoever it was minted for. A token a kiosk obtained
/// for the streaming API opens the management API just as well.
/// </para>
///
/// <para>
/// <b>What this asserts.</b> The options the nine APIs actually receive — built
/// through the real extension method, from an <em>empty</em> builder so no
/// <c>appsettings.json</c> and no ambient environment variable can supply the
/// answer. The authority URL is never dialled: <c>AddJwtBearer</c> fetches
/// metadata on the first request, not at configuration time.
/// </para>
///
/// <para>
/// <b>The literal, not a constant.</b> <c>"smart-sentinel-eye-api"</c> is
/// written out here and again in <c>RealmAudienceTests</c>, which reads it off
/// the realm file. Two independent spellings is the point — a constant asserted
/// against itself would pass whatever it was changed to, while these two fail
/// the moment the realm and the services disagree about which API a token names
/// (FR-009).
/// </para>
/// </summary>
public class BearerAudienceTests
{
    private const string ApiAudience = "smart-sentinel-eye-api";

    [Fact]
    public void The_bearer_options_validate_the_audience()
    {
        JwtBearerOptions options = BearerOptions();

        options.TokenValidationParameters.ValidateAudience.ShouldBeTrue(
            "with audience validation off, an API accepts any token this realm signed — "
            + "including one minted for a different API entirely (spec 069 FR-001).");
    }

    [Fact]
    public void The_bearer_options_name_this_products_api()
    {
        JwtBearerOptions options = BearerOptions();

        // Materialised through a null-coalesce because "no audience configured"
        // arrives as a null collection, and letting ShouldContain dereference it
        // reports a LINQ ArgumentNullException instead of the missing audience.
        IReadOnlyCollection<string> audiences =
            [.. options.TokenValidationParameters.ValidAudiences ?? []];

        audiences.ShouldContain(
            ApiAudience,
            customMessage: $"the services must require '{ApiAudience}'. Validating an audience "
            + "nothing names would refuse every token instead of the wrong ones (spec 069 FR-002).");
    }

    /// <summary>
    /// <b>The refusal itself, as a pure function.</b> The two assertions above
    /// describe configuration; this one exercises the decision. It calls the same
    /// <see cref="Validators.ValidateAudience"/> the bearer handler calls, so it
    /// needs no stack, no signing key and no minted token — and it fails today
    /// for the honest reason: with <c>ValidateAudience</c> off the function
    /// returns without looking.
    /// </summary>
    [Fact]
    public void A_token_minted_for_another_api_is_refused()
    {
        JwtBearerOptions options = BearerOptions();

        Should.Throw<SecurityTokenInvalidAudienceException>(() =>
            Validators.ValidateAudience(
                ["some-other-api"],
                securityToken: null,
                options.TokenValidationParameters));
    }

    /// <summary>
    /// The options the nine APIs receive, built through the real extension
    /// method. An <em>empty</em> builder deliberately: the default
    /// <c>Host.CreateApplicationBuilder</c> reads <c>appsettings.json</c> and the
    /// environment, either of which could supply an audience this test would then
    /// credit to <c>AddBearerAuthentication</c>.
    /// </summary>
    private static JwtBearerOptions BearerOptions()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
