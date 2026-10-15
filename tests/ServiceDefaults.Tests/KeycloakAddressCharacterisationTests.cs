using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Characterisation (spec 369, ADR-0144), captured green on unmodified
/// <c>AddBearerAuthentication</c> before it is split under ADR-0084's thirty
/// lines. Pins how the Keycloak address is found and what happens when it is
/// not: the lookup order, the null-only fall-through, the trimming of the
/// authority, the exception text, and that a refusal registers nothing.
///
/// <para>
/// Built from an <em>empty</em> builder so no <c>appsettings.json</c> or ambient
/// variable can supply an address. The environment is <c>Development</c> so that
/// an <c>http</c> or empty address resolves its options instead of failing the
/// framework's own https check, which is not what these tests are about.
/// Every expected string is a literal written here, never read from production.
/// </para>
/// </summary>
public class KeycloakAddressCharacterisationTests
{
    [Fact]
    public void The_connection_string_alone_is_the_address()
    {
        var authority = AuthorityOf(("ConnectionStrings:keycloak", "https://kc-connection.invalid"));

        authority.ShouldBe("https://kc-connection.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void The_http_service_key_alone_is_the_address()
    {
        var authority = AuthorityOf(("services:keycloak:http:0", "http://kc-http.invalid"));

        authority.ShouldBe("http://kc-http.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void The_https_service_key_alone_is_the_address()
    {
        var authority = AuthorityOf(("services:keycloak:https:0", "https://kc-https.invalid"));

        authority.ShouldBe("https://kc-https.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void The_connection_string_beats_both_service_keys()
    {
        var authority = AuthorityOf(
            ("ConnectionStrings:keycloak", "https://kc-connection.invalid"),
            ("services:keycloak:http:0", "http://kc-http.invalid"),
            ("services:keycloak:https:0", "https://kc-https.invalid"));

        authority.ShouldBe("https://kc-connection.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void The_http_service_key_beats_the_https_one()
    {
        var authority = AuthorityOf(
            ("services:keycloak:http:0", "http://kc-http.invalid"),
            ("services:keycloak:https:0", "https://kc-https.invalid"));

        authority.ShouldBe("http://kc-http.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void A_trailing_slash_is_trimmed_from_the_authority()
    {
        var authority = AuthorityOf(("ConnectionStrings:keycloak", "https://kc.invalid/"));

        authority.ShouldBe("https://kc.invalid/realms/smart-sentinel-eye");
    }

    [Fact]
    public void A_custom_resource_name_and_realm_read_their_own_connection_string()
    {
        var builder = EmptyBuilder(("ConnectionStrings:kc", "https://kc-custom.invalid"));

        builder.AddBearerAuthentication("kc", "other-realm");

        AuthorityOf(builder).ShouldBe("https://kc-custom.invalid/realms/other-realm");
    }

    [Fact]
    public void The_default_resource_name_does_not_read_a_differently_named_connection_string()
    {
        var builder = EmptyBuilder(("ConnectionStrings:kc", "https://kc-custom.invalid"));

        var thrown = Should.Throw<InvalidOperationException>(() => builder.AddBearerAuthentication());

        thrown.Message.ShouldBe(
            "Keycloak base URL not found. Looked for ConnectionStrings:keycloak, "
            + "services:keycloak:http:0, services:keycloak:https:0.");
    }

    [Fact]
    public void A_custom_resource_name_with_no_address_names_its_own_keys_in_the_message()
    {
        var builder = EmptyBuilder();

        var thrown = Should.Throw<InvalidOperationException>(() => builder.AddBearerAuthentication("kc"));

        thrown.Message.ShouldBe(
            "Keycloak base URL not found. Looked for ConnectionStrings:kc, "
            + "services:kc:http:0, services:kc:https:0.");
    }

    [Fact]
    public void No_address_throws_the_exact_message_from_the_call_itself()
    {
        var builder = EmptyBuilder();

        var thrown = Should.Throw<InvalidOperationException>(() => builder.AddBearerAuthentication());

        thrown.Message.ShouldBe(
            "Keycloak base URL not found. Looked for ConnectionStrings:keycloak, "
            + "services:keycloak:http:0, services:keycloak:https:0.");
    }

    [Fact]
    public void No_address_registers_nothing()
    {
        var builder = EmptyBuilder();
        var before = builder.Services.Count;

        Should.Throw<InvalidOperationException>(() => builder.AddBearerAuthentication());

        builder.Services.Count.ShouldBe(before);
    }

    /// <summary>
    /// Pinned, not endorsed (spec 369 J4): <c>??</c> falls through on null only,
    /// so an empty connection string is taken as the address. Outside
    /// Development the framework's own https check then refuses it at startup.
    /// </summary>
    [Fact]
    public void An_empty_connection_string_is_taken_as_found_and_does_not_fall_through()
    {
        var authority = AuthorityOf(
            ("ConnectionStrings:keycloak", string.Empty),
            ("services:keycloak:http:0", "http://kc-http.invalid"));

        authority.ShouldBe("/realms/smart-sentinel-eye");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_resource_name_is_refused(string name)
    {
        var builder = EmptyBuilder(("ConnectionStrings:keycloak", "https://kc.invalid"));

        Should.Throw<ArgumentException>(() => builder.AddBearerAuthentication(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_realm_is_refused(string realm)
    {
        var builder = EmptyBuilder(("ConnectionStrings:keycloak", "https://kc.invalid"));

        Should.Throw<ArgumentException>(() => builder.AddBearerAuthentication(realm: realm));
    }

    private static string AuthorityOf(params (string Key, string Value)[] settings)
    {
        var builder = EmptyBuilder(settings);
        builder.AddBearerAuthentication();
        return AuthorityOf(builder);
    }

    private static string AuthorityOf(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .Authority!;
    }

    private static HostApplicationBuilder EmptyBuilder(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        return builder;
    }
}
