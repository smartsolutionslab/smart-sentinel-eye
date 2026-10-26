using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authentication;

public sealed class SignedJwtClientCredentialsToStringTests
{
    [Fact]
    public void SignedJwtClientCredentials_ToString_does_not_contain_the_private_key()
    {
        const string privateKey = "cHJpdmF0ZS1rZXktMzE3MA==";
        var credentials = new SignedJwtClientCredentials("https://keycloak.test", "sse", "service-client", privateKey);

        credentials.ToString().ShouldNotContain(privateKey);
    }
}
