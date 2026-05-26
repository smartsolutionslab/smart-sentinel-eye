using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

public class AuthorizeWhepCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Authorize_with_a_valid_admin_token_returns_success()
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
    public async Task Authorize_with_a_token_missing_sse_management_returns_Forbidden()
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
        Domain.Stream.Stream stream = Domain.Stream.Stream.Provision(camera, AnAdmin, new FixedClock(FixedMoment));
        stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        stream.ReportDegraded("source unreachable", new FixedClock(FixedMoment.AddSeconds(15)));
        stream.ReportOffline("retry exhausted", new FixedClock(FixedMoment.AddMinutes(5)));
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
