using System.Globalization;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient.Fakes;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

/// <summary>
/// Hand-written fluent builder for
/// <see cref="RegisteredClientAggregate"/> per ADR-0054.
/// </summary>
public sealed class RegisteredClientBuilder
{
    private ClientId clientId = ClientId.From("plc-station-4");
    private ClientKind kind = ClientKind.Device;
    private FabIdentifier fab = FabIdentifier.From("munich");
    private OperatorIdentifier registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock clock = new(
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture));

    public RegisteredClientBuilder WithClientId(string raw) { clientId = ClientId.From(raw); return this; }
    public RegisteredClientBuilder WithKind(ClientKind kind) { this.kind = kind; return this; }
    public RegisteredClientBuilder WithFab(string fab) { this.fab = FabIdentifier.From(fab); return this; }
    public RegisteredClientBuilder WithRegisteredBy(OperatorIdentifier op) { registeredBy = op; return this; }
    public RegisteredClientBuilder WithClock(DateTimeOffset now) { clock = new FakeClock(now); return this; }

    public RegisteredClientAggregate Build() =>
        RegisteredClientAggregate.Register(clientId, kind, fab, registeredBy, clock);

    public FakeClock Clock => clock;
}
