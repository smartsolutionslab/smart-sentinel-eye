using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

public class AuthorizeWhepCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    /// <summary>
    /// The scopes an enrolled kiosk device holds — <c>KeycloakScopeBundles.Kiosk</c>,
    /// and what the browser kiosk's client grants from spec 041. Written out
    /// rather than referenced: Application tests do not reach into another
    /// bounded context, and <c>KioskScopeParityTests</c> is what keeps the two
    /// lists agreed.
    /// </summary>
    private static readonly string[] AKioskPersona =
    [
        "openid",
        "sse.cameras.read",
        "sse.streams.read",
        "sse.layouts.read",
        "sse.overlays.read",
        "sse.variables.read",
        "sse.events.write",
    ];

    /// <summary>
    /// management-web's actual token shape: the grandfathered bundle and
    /// <b>no</b> <c>sse.streams.read</c>. It is the case the fallback exists to
    /// keep working, and exactly what a naively-narrowed gate would break.
    /// </summary>
    [Fact]
    public async Task Authorize_with_a_grandfathered_management_token_returns_success()
    {
        FakeWhepAuthValidator validator = new()
        {
            Subject = Option<WhepAuthSubject>.Some(new WhepAuthSubject("admin-id", ["openid", "sse.management"])),
        };
        InMemoryStreamRepository streams = new();
        AuthorizeWhepCommandHandler handler = new(validator, streams);

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(SomeCamera()), "Bearer.xyz"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// <b>Spec 041.</b> Before this, the gate asked for the management bundle
    /// alone, so <em>no</em> kiosk could open a stream — not the browser kiosk,
    /// and not an enrolled device, whose bundle has never carried it. A kiosk
    /// that cannot watch video cannot do the only thing a kiosk is for.
    /// </summary>
    [Fact]
    public async Task Authorize_with_a_kiosk_token_returns_success()
    {
        FakeWhepAuthValidator validator = new()
        {
            Subject = Option<WhepAuthSubject>.Some(new WhepAuthSubject("kiosk-id", AKioskPersona)),
        };
        AuthorizeWhepCommandHandler handler = new(validator, new InMemoryStreamRepository());

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(SomeCamera()), "Bearer.kiosk"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Authorize_with_an_empty_token_returns_Unauthorized()
    {
        AuthorizeWhepCommandHandler handler = new(new FakeWhepAuthValidator(), new InMemoryStreamRepository());

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(SomeCamera()), ""),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeWhepError.Unauthorized>();
    }

    /// <summary>
    /// A token the validator could not attribute arrives here as
    /// <see cref="Option{T}.None"/> — including one carrying no <c>sub</c>,
    /// which is what a client with no sub mapper mints (spec 041). Still
    /// refused: an unattributable viewer stays refused.
    /// </summary>
    [Fact]
    public async Task Authorize_with_an_invalid_token_returns_Unauthorized()
    {
        FakeWhepAuthValidator validator = new() { Subject = Option<WhepAuthSubject>.None };
        AuthorizeWhepCommandHandler handler = new(validator, new InMemoryStreamRepository());

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(SomeCamera()), "Bearer.invalid"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeWhepError.Unauthorized>();
    }

    [Fact]
    public async Task Authorize_with_a_token_granting_neither_the_read_scope_nor_the_bundle_returns_Forbidden()
    {
        FakeWhepAuthValidator validator = new()
        {
            Subject = Option<WhepAuthSubject>.Some(new WhepAuthSubject("user-id", ["openid", "profile"])),
        };
        AuthorizeWhepCommandHandler handler = new(validator, new InMemoryStreamRepository());

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(SomeCamera()), "Bearer.scoped-wrong"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeWhepError.Forbidden>();
    }

    [Fact]
    public async Task Authorize_for_an_Offline_stream_returns_StreamUnavailable()
    {
        CameraIdentifier camera = SomeCamera();
        InMemoryStreamRepository streams = new();
        Domain.Stream.Stream stream = new StreamBuilder()
            .ForCamera(camera)
            .ProvisionedBy(AnAdmin)
            .At(FixedMoment)
            .Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        stream.ReportDegraded(StreamError.From("source unreachable"), new FixedClock(FixedMoment.AddSeconds(15)));
        stream.ReportOffline(StreamError.From("retry exhausted"), new FixedClock(FixedMoment.AddMinutes(5)));
        streams.Add(stream);
        await streams.SaveAsync(CancellationToken.None);

        FakeWhepAuthValidator validator = new()
        {
            Subject = Option<WhepAuthSubject>.Some(new WhepAuthSubject("admin-id", ["sse.management"])),
        };
        AuthorizeWhepCommandHandler handler = new(validator, streams);

        Result<MediaMtxPath, AuthorizeWhepError> result = await handler.HandleAsync(
            new AuthorizeWhepCommand(MediaMtxPath.For(camera), "Bearer.xyz"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeWhepError.StreamUnavailable>();
    }

    private static CameraIdentifier SomeCamera() => CameraIdentifier.From(Guid.CreateVersion7());
}
