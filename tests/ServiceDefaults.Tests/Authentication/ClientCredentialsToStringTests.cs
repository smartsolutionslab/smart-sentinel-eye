using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authentication;

public sealed class ClientCredentialsToStringTests
{
    [Fact]
    public void ClientCredentials_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-clientcreds-3169";
        var credentials = new ClientCredentials("https://keycloak.test", "sse", "service-client", secret);

        credentials.ToString().ShouldNotContain(secret);
    }
}
