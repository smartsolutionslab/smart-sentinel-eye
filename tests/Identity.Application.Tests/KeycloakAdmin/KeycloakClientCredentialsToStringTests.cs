using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Application.Tests.KeycloakAdmin;

public sealed class KeycloakClientCredentialsToStringTests
{
    [Fact]
    public void KeycloakClientCredentials_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-keycloak-3169";
        var credentials = new KeycloakClientCredentials(secret);

        credentials.ToString().ShouldNotContain(secret);
    }
}
