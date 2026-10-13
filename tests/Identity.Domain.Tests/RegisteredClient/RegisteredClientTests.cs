using System.Globalization;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.RegisteredClient.Events;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class RegisteredClientTests
{
    private static readonly DateTimeOffset Created =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Register_starts_in_Active_and_raises_ClientRegisteredDomainEvent()
    {
        var client = new RegisteredClientBuilder().WithClock(Created).Build();

        client.DisabledAt.ShouldBeNull();
        client.LastRotatedAt.ShouldBeNull();
        client.Registration.At.Value.ShouldBe(Created);
        client.ClientId.Value.ShouldBe("plc-station-4");
        client.Kind.ShouldBe(ClientKind.Device);
        client.Fab.Value.ShouldBe("munich");

        client.PendingEvents.OfType<ClientRegisteredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Register_records_when_it_happened_and_who_did_it()
    {
        var registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());

        var client = new RegisteredClientBuilder()
            .WithClock(Created)
            .WithRegisteredBy(registeredBy)
            .Build();

        client.Registration.At.Value.ShouldBe(Created);
        client.Registration.By.ShouldBe(registeredBy);
    }

    [Fact]
    public void Disable_stamps_DisabledAt_and_raises_ClientDisabledDomainEvent()
    {
        var client = new RegisteredClientBuilder().WithClock(Created).Build();
        client.ClearPendingEvents();

        var disabledMoment = Created.AddHours(1);
        client.Disable(new FakeClock(disabledMoment));

        client.DisabledAt!.Value.ShouldBe(disabledMoment);
        client.PendingEvents.OfType<ClientDisabledDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Disable_is_idempotent_on_an_already_disabled_client()
    {
        var client = new RegisteredClientBuilder().WithClock(Created).Build();
        client.Disable(new FakeClock(Created.AddHours(1)));
        client.ClearPendingEvents();

        client.Disable(new FakeClock(Created.AddHours(2)));

        client.DisabledAt!.Value.ShouldBe(Created.AddHours(1)); // unchanged
        client.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Rotate_on_a_webhook_integration_stamps_LastRotatedAt_and_raises_ClientRotatedDomainEvent()
    {
        var client = new RegisteredClientBuilder()
            .WithKind(ClientKind.WebhookIntegration)
            .WithClientId("webhook-qa")
            .WithClock(Created)
            .Build();
        client.ClearPendingEvents();

        var rotatedMoment = Created.AddHours(2);
        client.Rotate(new FakeClock(rotatedMoment));

        client.LastRotatedAt!.Value.ShouldBe(rotatedMoment);
        client.PendingEvents.OfType<ClientRotatedDomainEvent>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Device")]
    [InlineData("Kiosk")]
    public void Rotate_throws_on_non_webhook_clients(string kindValue)
    {
        var client = new RegisteredClientBuilder()
            .WithKind(ClientKind.From(kindValue))
            .WithClock(Created)
            .Build();

        Action act = () => client.Rotate(new FakeClock(Created.AddHours(1)));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Rotate_throws_on_a_disabled_client()
    {
        var client = new RegisteredClientBuilder()
            .WithKind(ClientKind.WebhookIntegration)
            .WithClock(Created)
            .Build();
        client.Disable(new FakeClock(Created.AddHours(1)));

        Action act = () => client.Rotate(new FakeClock(Created.AddHours(2)));
        act.ShouldThrow<InvalidOperationException>();
    }
}
