using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.EventHandlers;

/// <summary>
/// #2789. The retry of a failed race-path disable: the handler for
/// <see cref="WebhookClientRaceDisableFailed"/> repeats the same disable the
/// revocation handler performs, and throws the same
/// <see cref="WebhookClientDisableFailedException"/> so the same retry ladder
/// applies. Mirrors <c>WebhookIntegrationRevokedIntegrationEventHandlerTests</c>.
/// </summary>
public class WebhookClientRaceDisableFailedHandlerTests
{
    [Fact]
    public async Task Sends_DisableWebhookClientCommand_with_the_derived_clientId_and_the_messages_fab()
    {
        RecordingDisableWebhookClientCommandHandler commands = new();
        WebhookClientRaceDisableFailedHandler subscriber = new(
            commands, new CapturingLogger<WebhookClientRaceDisableFailedHandler>());

        await subscriber.Handle(new WebhookClientRaceDisableFailed("qa", "munich"), CancellationToken.None);

        var sent = commands.Sent.ShouldHaveSingleItem();
        sent.ClientId.ShouldBe(ClientId.From("webhook-qa"));
        sent.Fab.ShouldBe(FabIdentifier.From("munich"));
    }

    [Fact]
    public async Task WebhookClientNotFound_returns_without_throwing_and_logs_that_there_is_nothing_to_disable()
    {
        RecordingDisableWebhookClientCommandHandler commands = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                DisableWebhookClientFailures.WebhookClientNotFound("webhook-qa")),
        };
        CapturingLogger<WebhookClientRaceDisableFailedHandler> logger = new();
        WebhookClientRaceDisableFailedHandler subscriber = new(commands, logger);

        await subscriber.Handle(new WebhookClientRaceDisableFailed("qa", "munich"), CancellationToken.None);

        commands.Sent.ShouldHaveSingleItem();
        logger.Named("NoWebhookClientToDisable").ShouldHaveSingleItem(
            "an already disabled client is the retry's goal reached, and an operator should be able to see that");
    }

    [Fact]
    public async Task KeycloakUnavailable_throws_the_disable_failure_the_retry_policy_matches()
    {
        RecordingDisableWebhookClientCommandHandler commands = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                DisableWebhookClientFailures.KeycloakUnavailable("transport failure")),
        };
        WebhookClientRaceDisableFailedHandler subscriber = new(
            commands, new CapturingLogger<WebhookClientRaceDisableFailedHandler>());

        (await Should.ThrowAsync<Exception>(() => subscriber.Handle(
            new WebhookClientRaceDisableFailed("qa", "munich"), CancellationToken.None)))
            .ShouldBeOfType<WebhookClientDisableFailedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not_A_Valid_Fab!")]
    public async Task A_null_blank_or_unparsable_fab_is_dropped_without_calling_the_command_and_is_logged(string? fab)
    {
        RecordingDisableWebhookClientCommandHandler commands = new();
        CapturingLogger<WebhookClientRaceDisableFailedHandler> logger = new();
        WebhookClientRaceDisableFailedHandler subscriber = new(commands, logger);

        await subscriber.Handle(new WebhookClientRaceDisableFailed("qa", fab!), CancellationToken.None);

        commands.Sent.ShouldBeEmpty();
        var entry = logger.Named("RevocationFabInvalid").ShouldHaveSingleItem(
            "a message that can never succeed is dropped, not retried, but it must leave a trace");
        entry.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task An_integration_name_that_cannot_form_a_valid_ClientId_is_dropped_without_calling_the_command_and_is_logged()
    {
        RecordingDisableWebhookClientCommandHandler commands = new();
        CapturingLogger<WebhookClientRaceDisableFailedHandler> logger = new();
        WebhookClientRaceDisableFailedHandler subscriber = new(commands, logger);

        await subscriber.Handle(new WebhookClientRaceDisableFailed("bad name", "munich"), CancellationToken.None);

        commands.Sent.ShouldBeEmpty();
        logger.Named("InvalidRevocationName").ShouldHaveSingleItem(
            "a name that can never form a ClientId must be dropped and logged, not retried forever");
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
