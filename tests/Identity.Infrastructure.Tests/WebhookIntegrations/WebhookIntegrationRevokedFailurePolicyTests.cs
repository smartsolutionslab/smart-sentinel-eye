using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.WebhookIntegrations;

/// <summary>
/// Spec 328 (#2629). Proves — by actually running Wolverine's handler discovery
/// against the real <c>Identity.Application</c> assembly, not by reading source
/// text — that <see cref="WebhookIntegrationRevokedFailurePolicy"/> gives
/// <see cref="WebhookIntegrationRevokedV1"/>'s own handler chain a retry-with-
/// cooldown ladder (1 min, 2 min, then 5 min x 12 — 14 scheduled retries, 15
/// attempts in all) for exactly <see cref="WebhookClientDisableFailedException"/>,
/// that attempt 15 dead-letters, that the rule does not widen to a plain
/// <see cref="InvalidOperationException"/> or other failures, that the real
/// handler's own throw site is the exception the rule matches, and that no
/// other discovered chain gained a failure rule of its own. Mirrors
/// <c>WallSceneSwitchFailurePolicyTests</c> (spec 296) — same discovery
/// technique, same reflection-based read of an internal continuation's
/// <c>Delay</c>.
///
/// <para>
/// <b>How this runs without a live Postgres or RabbitMQ</b> (ADR-0103):
/// <see cref="WolverineOptions.StubAllExternalTransports"/>, plus never calling
/// <c>PersistMessagesWithPostgresql</c>, lets <c>UseWolverine</c> compile real
/// handler chains in-process. Discovery is driven off the real
/// <c>Identity.Application</c> assembly and
/// <see cref="IdentityInfrastructureModule.ConfigureMessageHandling"/> itself —
/// not a direct <c>Policies.Add&lt;...&gt;()</c> call — so this test also
/// covers the exact registration production uses (plan.md §4/§5.1).
/// </para>
///
/// <para>
/// <b>Counterfactuals (plan.md §5.2, run once the policy is implemented, then
/// reverted — not run by this file):</b> swapping <c>ScheduleRetry</c> for
/// <c>RetryWithCooldown</c> turns the ladder fact red on the continuation's type
/// name; widening any one 5-minute step to 10 minutes turns it red on that
/// attempt's delay; removing the <c>Policies.Add</c> call from
/// <c>ConfigureMessageHandling</c> turns all four red facts below red again;
/// reverting the handler to throw a bare <c>InvalidOperationException</c> turns
/// the link fact (and <c>Identity.Application.Tests</c>' own fact) red again.
/// </para>
/// </summary>
public class WebhookIntegrationRevokedFailurePolicyTests
{
    private static async Task<HandlerGraph> DiscoverAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(WebhookIntegrationRevokedIntegrationEventHandler).Assembly);
            IdentityInfrastructureModule.ConfigureMessageHandling(opts);
            opts.StubAllExternalTransports();
        });

        using IHost host = builder.Build();
        await host.StartAsync();
        HandlerGraph graph = host.Services.GetRequiredService<HandlerGraph>();
        await host.StopAsync();
        return graph;
    }

    /// <summary>
    /// Reads the internal <c>ScheduledRetryContinuation</c>'s public <c>Delay</c>
    /// property by reflection — the type itself is internal to Wolverine, and
    /// reading <c>Delay</c> directly is culture-free unlike parsing
    /// <c>ToString()</c> (plan.md §5.1, mirroring the <c>MoveToErrorQueue</c>
    /// type-name technique <c>WallSceneSwitchFailurePolicyTests</c> already uses).
    /// </summary>
    private static TimeSpan ReadDelay(IContinuation continuation)
    {
        PropertyInfo? property = continuation.GetType().GetProperty("Delay", BindingFlags.Public | BindingFlags.Instance);
        property.ShouldNotBeNull($"{continuation.GetType().Name} is expected to expose a public Delay property");
        object? value = property.GetValue(continuation);
        return value.ShouldBeOfType<TimeSpan>();
    }

    [Fact]
    public async Task A_failed_disable_is_retried_after_one_minute_then_two_then_five_minutes_until_attempt_fourteen()
    {
        HandlerGraph graph = await DiscoverAsync();
        HandlerChain? chain = graph.ChainFor<WebhookIntegrationRevokedV1>();
        chain.ShouldNotBeNull();

        FailureRule rule = chain.Failures.ShouldHaveSingleItem();

        List<TimeSpan> expectedLadder =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            .. Enumerable.Repeat(TimeSpan.FromMinutes(5), 12),
        ];
        expectedLadder.Count.ShouldBe(14);

        for (int attempt = 1; attempt <= 14; attempt++)
        {
            Envelope envelope = new(SampleMessage()) { Attempts = attempt };
            bool created = rule.TryCreateContinuation(
                new WebhookClientDisableFailedException("w", "KEYCLOAK_UNAVAILABLE"), envelope, out IContinuation continuation);

            created.ShouldBeTrue($"attempt {attempt} should still be on the retry ladder");
            continuation.ShouldNotBeNull();
            continuation.GetType().Name.ShouldBe(
                "ScheduledRetryContinuation",
                $"attempt {attempt} should schedule a retry rather than dead-letter or retry inline");
            ReadDelay(continuation).ShouldBe(
                expectedLadder[attempt - 1],
                $"attempt {attempt}'s cooldown should match the 1m, 2m, 5m x12 ladder");
        }
    }

    [Fact]
    public async Task The_fifteenth_failed_attempt_is_dead_lettered()
    {
        HandlerGraph graph = await DiscoverAsync();
        HandlerChain? chain = graph.ChainFor<WebhookIntegrationRevokedV1>();
        chain.ShouldNotBeNull();

        FailureRule rule = chain.Failures.ShouldHaveSingleItem();

        Envelope envelope = new(SampleMessage()) { Attempts = 15 };
        bool created = rule.TryCreateContinuation(
            new WebhookClientDisableFailedException("w", "KEYCLOAK_UNAVAILABLE"), envelope, out IContinuation continuation);

        created.ShouldBeTrue();
        continuation.ShouldNotBeNull();
        continuation.GetType().Name.ShouldBe(
            "MoveToErrorQueue",
            "the 15th attempt is past the 14-slot ladder and must dead-letter, not retry again");
    }

    [Fact]
    public async Task The_rule_matches_the_disable_failure_and_nothing_wider()
    {
        HandlerGraph graph = await DiscoverAsync();
        HandlerChain? chain = graph.ChainFor<WebhookIntegrationRevokedV1>();
        chain.ShouldNotBeNull();

        FailureRule rule = chain.Failures.ShouldHaveSingleItem();

        rule.Match.Matches(new WebhookClientDisableFailedException("w", "KEYCLOAK_UNAVAILABLE")).ShouldBeTrue();
        rule.Match.Matches(new InvalidOperationException()).ShouldBeFalse(
            "scoped to WebhookClientDisableFailedException specifically, not every InvalidOperationException it derives from");
        rule.Match.Matches(new DbUpdateException()).ShouldBeFalse();
        rule.Match.Matches(new OperationCanceledException()).ShouldBeFalse();
    }

    [Fact]
    public async Task The_exception_the_handler_throws_on_KeycloakUnavailable_is_the_one_the_rule_matches()
    {
        HandlerGraph graph = await DiscoverAsync();
        HandlerChain? chain = graph.ChainFor<WebhookIntegrationRevokedV1>();
        chain.ShouldNotBeNull();

        FailureRule rule = chain.Failures.ShouldHaveSingleItem();

        StubDisableWebhookClientCommandHandler handler = new()
        {
            Result = Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                DisableWebhookClientFailures.KeycloakUnavailable("transport failure")),
        };
        WebhookIntegrationRevokedIntegrationEventHandler subscriber = new(
            handler, NullLogger<WebhookIntegrationRevokedIntegrationEventHandler>.Instance);

        Exception caught = await Should.ThrowAsync<Exception>(
            () => subscriber.Handle(SampleMessage(), CancellationToken.None));

        rule.Match.Matches(caught).ShouldBeTrue(
            "the real handler's own throw site must be exactly what the policy's rule matches");
    }

    [Fact]
    public async Task No_other_discovered_chain_gained_a_failure_rule()
    {
        HandlerGraph graph = await DiscoverAsync();

        HandlerChain[] others = [.. graph.AllChains()
            .Where(chain => chain.MessageType != typeof(WebhookIntegrationRevokedV1))];
        others.ShouldNotBeEmpty("otherwise this proves nothing about scoping");

        foreach (HandlerChain chain in others)
        {
            chain.Failures.ShouldBeEmpty(
                $"{chain.MessageType.Name}'s chain gained a failure rule nothing asked it to have");
        }
    }

    private static WebhookIntegrationRevokedV1 SampleMessage() => new(
        "w",
        DateTimeOffset.Parse("2026-05-29T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        new EventMetadata(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "munich", null));

    private sealed class StubDisableWebhookClientCommandHandler
        : ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>
    {
        public Result<RegisteredClientIdentifier, DisableWebhookClientError> Result { get; set; } =
            Result<RegisteredClientIdentifier, DisableWebhookClientError>.Success(RegisteredClientIdentifier.New());

        public Task<Result<RegisteredClientIdentifier, DisableWebhookClientError>> HandleAsync(
            DisableWebhookClientCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }
}
