using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.EventHandlers;

/// <summary>
/// Spec 264 (#2206). Shape mirrors <c>CameraRetiredIntegrationEventHandler</c>
/// (StreamDistribution): translate the integration event into a command, drop
/// what can never succeed, throw on what Wolverine should retry (plan.md
/// §5.3). Red: compile, until <c>WebhookIntegrationRevokedIntegrationEventHandler</c>
/// and <c>DisableWebhookClientCommand</c> exist.
/// </summary>
public class WebhookIntegrationRevokedIntegrationEventHandlerTests
{
    private static readonly DateTimeOffset RevokedAtMoment =
        DateTimeOffset.Parse("2026-05-29T10:00:00Z", CultureInfo.InvariantCulture);

    private static EventMetadata MetadataWithFab(string? fab) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        RevokedAtMoment, fab, null);

    [Fact]
    public async Task Sends_DisableWebhookClientCommand_with_the_derived_clientId_and_the_messages_fab()
    {
        RecordingDisableWebhookClientCommandHandler handler = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Success(
                RegisteredClientIdentifier.New()),
        };
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        await subscriber.Handle(
            new WebhookIntegrationRevokedV1("w", RevokedAtMoment, MetadataWithFab("munich")),
            CancellationToken.None);

        DisableWebhookClientCommand sent = handler.Sent.ShouldHaveSingleItem();
        sent.ClientId.ShouldBe(ClientId.From("webhook-w"));
        sent.Fab.ShouldBe(FabIdentifier.From("munich"));
    }

    [Fact]
    public async Task WebhookClientNotFound_returns_without_throwing()
    {
        RecordingDisableWebhookClientCommandHandler handler = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                DisableWebhookClientFailures.WebhookClientNotFound("webhook-w")),
        };
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        await subscriber.Handle(
            new WebhookIntegrationRevokedV1("w", RevokedAtMoment, MetadataWithFab("munich")),
            CancellationToken.None);

        handler.Sent.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task KeycloakUnavailable_throws_so_Wolverine_retries()
    {
        RecordingDisableWebhookClientCommandHandler handler = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                DisableWebhookClientFailures.KeycloakUnavailable("transport failure")),
        };
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => subscriber.Handle(
            new WebhookIntegrationRevokedV1("w", RevokedAtMoment, MetadataWithFab("munich")),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not_A_Valid_Fab!")]
    public async Task A_null_blank_or_unparsable_fab_is_dropped_without_calling_the_command(string? fab)
    {
        RecordingDisableWebhookClientCommandHandler handler = new();
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        await subscriber.Handle(
            new WebhookIntegrationRevokedV1("w", RevokedAtMoment, MetadataWithFab(fab)),
            CancellationToken.None);

        handler.Sent.ShouldBeEmpty();
    }

    /// <summary>
    /// A name that cannot form a valid <c>ClientId</c> (spaces are not one of
    /// its allowed characters) must be dropped rather than throwing — a
    /// message that can never succeed must not be retried forever.
    /// </summary>
    [Fact]
    public async Task An_integration_name_that_cannot_form_a_valid_ClientId_is_dropped_without_calling_the_command()
    {
        RecordingDisableWebhookClientCommandHandler handler = new();
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        await subscriber.Handle(
            new WebhookIntegrationRevokedV1("bad name", RevokedAtMoment, MetadataWithFab("munich")),
            CancellationToken.None);

        handler.Sent.ShouldBeEmpty();
    }

    private sealed class RecordingDisableWebhookClientCommandHandler
        : ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>
    {
        public List<DisableWebhookClientCommand> Sent { get; } = [];

        public Result<RegisteredClientIdentifier, DisableWebhookClientError> Result { get; set; } =
            Result<RegisteredClientIdentifier, DisableWebhookClientError>.Success(RegisteredClientIdentifier.New());

        public Task<Result<RegisteredClientIdentifier, DisableWebhookClientError>> HandleAsync(
            DisableWebhookClientCommand command, CancellationToken cancellationToken)
        {
            Sent.Add(command);
            return Task.FromResult(Result);
        }
    }
}
