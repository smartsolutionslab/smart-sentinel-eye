using SmartSentinelEye.Identity.Application.DTOs;

namespace SmartSentinelEye.Identity.Application.Tests.DTOs;

public sealed class CredentialDtoToStringTests
{
    [Fact]
    public void DeviceCredentialsDto_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-device-3159";
        var dto = new DeviceCredentialsDto(Guid.NewGuid(), "device-client", "camera", "device-1", "fab-a", secret);

        dto.ToString().ShouldNotContain(secret);
    }

    [Fact]
    public void KioskCredentialsDto_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-kiosk-3159";
        var dto = new KioskCredentialsDto(Guid.NewGuid(), "kiosk-client", "fab-a", secret);

        dto.ToString().ShouldNotContain(secret);
    }

    [Fact]
    public void WebhookClientCredentialsDto_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-webhook-3159";
        var dto = new WebhookClientCredentialsDto(Guid.NewGuid(), 2, "webhook-client", "integration-1", "fab-a", secret);

        dto.ToString().ShouldNotContain(secret);
    }

    [Fact]
    public void ReplayedClientDto_ToString_does_not_contain_the_client_secret()
    {
        const string secret = "s3cr3t-replayed-3159";
        var dto = new ReplayedClientDto(Guid.NewGuid(), 2, "replayed-client", "fab-a", secret);

        dto.ToString().ShouldNotContain(secret);
    }
}
