using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Spec 071 — the WHEP hook checks the audience too.
///
/// <para>
/// <b>What is broken.</b> <c>WhepAuthValidator</c> builds its
/// <see cref="TokenValidationParameters"/> with <c>ValidateAudience = false</c>,
/// under a comment claiming it mirrors the standard bearer pipeline. That comment
/// stopped being true when #91 merged and set <c>ValidAudiences</c> on
/// <c>AuthenticationDefaults</c>, leaving <c>POST /streams/authorize</c> as the one
/// authenticated HTTP surface that still accepts a token minted for another API.
/// </para>
///
/// <para>
/// <b>Why these tests exist at all.</b> This construction has never had unit-level
/// cover: it was reachable only through <c>WhepAuthIntegrationTests</c>, which needs
/// Docker — a large part of why the flag could go stale with nothing noticing. These
/// run in seconds, with no stack, no signing key, no minted token, and no network
/// call: the authority below is never dialled.
/// </para>
///
/// <para>
/// <b>The pairing, not a comment.</b> A comment was the only thing binding the hook
/// to the pipeline and it did not survive one change. The parity tests below compare
/// the two sides directly, so either one moving is a failure here rather than a
/// discovery in production (spec 071 FR-005).
/// </para>
/// </summary>
public class WhepAudienceTests
{
    /// <summary>
    /// Any well-formed authority; the parameters are inspected, never used to fetch
    /// metadata, so nothing resolves this host.
    /// </summary>
    private const string Authority = "https://keycloak.invalid/realms/smart-sentinel-eye";

    /// <summary>
    /// <b>The refusal itself, as a pure function.</b> Calls the same
    /// <see cref="Validators.ValidateAudience"/> the bearer handler calls, exactly as
    /// spec 069's <c>BearerAudienceTests.A_token_minted_for_another_api_is_refused</c>
    /// does. With <c>ValidateAudience</c> off the function returns without looking,
    /// which is the honest reason this fails today.
    /// </summary>
    [Fact]
    public void A_whep_token_minted_for_another_api_is_refused()
    {
        Should.Throw<SecurityTokenInvalidAudienceException>(() =>
            Validators.ValidateAudience(
                ["some-other-api"],
                securityToken: null,
                WhepAuthValidator.CreateParameters(Authority)));
    }

    /// <summary>
    /// Parity on the switch. Compared against the pipeline rather than asserted as
    /// <c>true</c>, so the two cannot drift apart in either direction.
    /// </summary>
    [Fact]
    public void The_whep_hook_validates_the_audience_exactly_as_the_bearer_pipeline_does()
    {
        TokenValidationParameters whep = WhepAuthValidator.CreateParameters(Authority);
        TokenValidationParameters bearer = BearerOptions().TokenValidationParameters;

        whep.ValidateAudience.ShouldBe(
            bearer.ValidateAudience,
            customMessage: "the WHEP hook accepts tokens the nine APIs would refuse. A comment "
            + "claiming it mirrors them is the only thing that ever bound them, and it did not "
            + "survive one change (spec 071 FR-005).");
    }

    /// <summary>
    /// Parity on the audience itself. Both sides are materialised through a
    /// null-coalesce first: "no audience configured" arrives as a <b>null</b>
    /// collection, and letting Shouldly dereference it reports an
    /// <c>ArgumentNullException</c> instead of the missing audience — a trap that
    /// already cost spec 069 a debugging round.
    /// </summary>
    [Fact]
    public void The_whep_hook_names_the_same_api_as_the_bearer_pipeline()
    {
        IReadOnlyCollection<string> whep =
            [.. WhepAuthValidator.CreateParameters(Authority).ValidAudiences ?? []];
        IReadOnlyCollection<string> bearer =
            [.. BearerOptions().TokenValidationParameters.ValidAudiences ?? []];

        whep.ShouldBe(
            bearer,
            ignoreOrder: true,
            customMessage: "the WHEP hook and the nine APIs must name the same API. Read the "
            + "audience off the constant the bearer pipeline reads, so this hook cannot accept "
            + "a token they would refuse (spec 071 FR-002).");
    }

    /// <summary>
    /// <b>The over-correction guard — green on arrival, by declaration</b> (plan.md
    /// Declaration 3). Green today for the wrong reason (validation is off) and green
    /// after the fix for the right one. It exists so the refusal above cannot be
    /// bought by validating an audience nothing names, which would 401 every kiosk in
    /// the fab.
    /// </summary>
    [Fact]
    public void A_whep_token_minted_for_this_api_is_accepted()
    {
        Should.NotThrow(() =>
            Validators.ValidateAudience(
                [AuthenticationDefaults.ApiAudience],
                securityToken: null,
                WhepAuthValidator.CreateParameters(Authority)));
    }

    /// <summary>
    /// The options the nine APIs receive, built through the real extension method.
    /// An <em>empty</em> builder deliberately: the default
    /// <c>Host.CreateApplicationBuilder</c> reads <c>appsettings.json</c> and the
    /// environment, either of which could supply an audience this test would then
    /// credit to <c>AddBearerAuthentication</c>. Mirrors
    /// <c>BearerAudienceTests.BearerOptions</c>.
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
