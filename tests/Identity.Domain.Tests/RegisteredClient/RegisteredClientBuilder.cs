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
    private ClientId _clientId = ClientId.From("plc-station-4");
    private ClientKind _kind = ClientKind.Device;
    private FabIdentifier _fab = FabIdentifier.From("munich");
    private OperatorIdentifier _registeredBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture));

    public RegisteredClientBuilder WithClientId(string raw) { _clientId = ClientId.From(raw); return this; }
    public RegisteredClientBuilder WithKind(ClientKind kind) { _kind = kind; return this; }
    public RegisteredClientBuilder WithFab(string fab) { _fab = FabIdentifier.From(fab); return this; }
    public RegisteredClientBuilder WithRegisteredBy(OperatorIdentifier op) { _registeredBy = op; return this; }
    public RegisteredClientBuilder WithClock(DateTimeOffset now) { _clock = new FakeClock(now); return this; }

    public RegisteredClientAggregate Build() =>
        RegisteredClientAggregate.Register(_clientId, _kind, _fab, _registeredBy, _clock);

    public FakeClock Clock => _clock;
}
