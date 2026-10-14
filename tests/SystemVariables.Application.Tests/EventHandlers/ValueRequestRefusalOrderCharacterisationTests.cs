using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>SystemVariableValueRequestedV1Handler.Handle</c> is
/// split into helpers. The refactor must leave the order of the refusals, which
/// of them consume a dedup reservation, the words each one logs and the command
/// the success path builds exactly as they are.
///
/// <para>
/// Every log line below is a hand-written literal with a distinct value in each
/// slot (a different name, fab and causing event in every case), so a call that
/// transposes two arguments of the same type changes the text. Counterfactual
/// seen to fail: swapping <c>metadata.Fab</c> and <c>name</c> in the
/// <c>ValueRequestWithUnusableFab</c> call turns
/// <see cref="An_unusable_fab_is_dropped_before_any_reservation_and_names_fab_name_and_cause"/>
/// red.
/// </para>
///
/// <para>
/// Spec 349 section 6 oddity (c) was fixed by spec 358: the name is validated
/// before the reservation is taken, so an invalid name consumes no dedup key
/// (<see cref="An_invalid_name_is_dropped_before_any_reservation"/>).
/// </para>
/// </summary>
public class ValueRequestRefusalOrderCharacterisationTests
{
    private const string CausingEvent = "3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13";
    private const string OtherCausingEvent = "8f4a2b6c-1e9d-47a3-b5c0-d2e8f7a61b94";

    private static readonly Guid Causing = Guid.Parse(CausingEvent);
    private static readonly Guid OtherCausing = Guid.Parse(OtherCausingEvent);

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset Requested =
        DateTimeOffset.Parse("2026-05-28T08:14:30Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset Accepted =
        DateTimeOffset.Parse("2026-05-28T08:14:29.125Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_request_with_no_metadata_is_dropped_with_the_no_fab_warning_and_no_reservation()
    {
        var run = await RunAsync("speedLine7", "12", Causing, metadata: null);

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Value request for 'speedLine7' carries no fab; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_request_whose_metadata_names_no_fab_is_dropped_with_the_no_fab_warning_and_no_reservation()
    {
        var run = await RunAsync("speedLine7", "12", Causing, MetadataFor(fab: null));

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Value request for 'speedLine7' carries no fab; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
    }

    [Fact]
    public async Task A_whitespace_fab_takes_the_no_fab_branch_not_the_unusable_one()
    {
        var run = await RunAsync("speedLine7", "12", Causing, MetadataFor(fab: "   "));

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Value request for 'speedLine7' carries no fab; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
    }

    [Fact]
    public async Task An_unusable_fab_is_dropped_before_any_reservation_and_names_fab_name_and_cause()
    {
        var run = await RunAsync("speedLine7", "12", Causing, MetadataFor(fab: "Bad_Fab"));

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Value request for 'speedLine7' carries unusable fab 'Bad_Fab'; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
        entry.Exception.ShouldNotBeNull().GetType().ShouldBe(typeof(ArgumentException));
        entry.Exception.Message.ShouldBe(
            "value: must be lowercase letters, digits, or '-' and start with a letter (Parameter 'value')");
    }

    [Fact]
    public async Task A_missing_fab_is_refused_before_an_invalid_name_is_looked_at()
    {
        var run = await RunAsync("1bad", "12", Causing, MetadataFor(fab: null));

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe(
            "Value request for '1bad' carries no fab; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
    }

    [Fact]
    public async Task An_unusable_fab_is_refused_before_an_invalid_name_and_before_a_duplicate()
    {
        var run = await RunAsync("1bad", "12", Causing, MetadataFor(fab: "X"), reserves: false);

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe(
            "Value request for '1bad' carries unusable fab 'X'; dropping (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
        entry.Exception.ShouldNotBeNull().Message.ShouldBe(
            "value must be at least 2 character(s). (Parameter 'value')");
    }

    [Fact]
    public async Task A_duplicate_is_dropped_at_debug_after_one_reservation_attempt()
    {
        var run = await RunAsync("oeeLine1", "82.5", Causing, MetadataFor("munich"), reserves: false);

        run.Dedup.Calls.ShouldBe(["munich|oeeLine1|3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13"]);
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe(
            "Dedup hit for variable 'oeeLine1' in fab 'munich' caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13; no-op.");
    }

    [Fact]
    public async Task An_invalid_name_is_reported_as_invalid_even_when_its_key_would_be_a_duplicate()
    {
        var run = await RunAsync("1bad", "82.5", OtherCausing, MetadataFor("dresden"), reserves: false);

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Invalid variable name '1bad' in V1; dropping (caused by 8f4a2b6c-1e9d-47a3-b5c0-d2e8f7a61b94).");
    }

    [Fact]
    public async Task An_invalid_name_is_dropped_before_any_reservation()
    {
        var run = await RunAsync("1bad", "82.5", OtherCausing, MetadataFor("dresden"));

        run.Dedup.Calls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Invalid variable name '1bad' in V1; dropping (caused by 8f4a2b6c-1e9d-47a3-b5c0-d2e8f7a61b94).");
        entry.Exception.ShouldNotBeNull().GetType().ShouldBe(typeof(ArgumentException));
        entry.Exception.Message.ShouldBe(
            "value: must start with a letter and contain only letters, digits, and underscores (Parameter 'value')");
    }

    [Fact]
    public async Task A_variable_that_only_another_fab_holds_is_dropped_naming_the_requesting_fab()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(NumberVariable("munich", "oeeLine1", 1));

        var run = await RunAsync("oeeLine1", "82.5", Causing, MetadataFor("dresden"), repo: repo);

        run.Dedup.Calls.ShouldBe(["dresden|oeeLine1|3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13"]);
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Variable 'oeeLine1' is not defined in fab 'dresden'; value change dropped (caused by 3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13).");
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_dropped_with_the_failure_code_in_the_last_slot()
    {
        InMemoryVariableRepository repo = new();
        var variable = NumberVariable("munich", "oeeLine1", 1);
        repo.Add(variable);

        var run = await RunAsync("oeeLine1", "not-a-number", OtherCausing, MetadataFor("munich"), repo: repo);

        run.Dedup.Calls.ShouldBe(["munich|oeeLine1|8f4a2b6c-1e9d-47a3-b5c0-d2e8f7a61b94"]);
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "SetVariableValue failed for 'oeeLine1' = 'not-a-number' (caused by 8f4a2b6c-1e9d-47a3-b5c0-d2e8f7a61b94): VARIABLE_TYPE_MISMATCH.");
        variable.Value.ShouldBe(new VariableValue.NumberValue(1));
    }

    [Fact]
    public async Task A_success_logs_nothing_and_attributes_the_change_to_the_automation_operator()
    {
        InMemoryVariableRepository repo = new();
        var variable = NumberVariable("munich", "oeeLine1", 1);
        repo.Add(variable);

        var run = await RunAsync("oeeLine1", "82.5", Causing, MetadataFor("munich"), repo: repo);

        run.Logger.Entries.ShouldBeEmpty();
        run.Dedup.Calls.ShouldBe(["munich|oeeLine1|3c1d5e7a-9b2f-4d60-8a41-7e5b0c2d9f13"]);
        variable.Value.ShouldBe(new VariableValue.NumberValue(82.5));
        var change = run.Changes.ShouldHaveSingleItem();
        change.ChangedBy.Value.ShouldBe(Guid.Parse("a07a07a0-7000-7000-8000-000000000007"));
        change.Fab.Value.ShouldBe("munich");
        change.Name.Value.ShouldBe("oeeLine1");
        change.Value.ShouldBe(new VariableValue.NumberValue(82.5));
        change.ChangedAt.ShouldBe(Moment);
    }

    [Fact]
    public void The_automation_operator_is_the_fixed_well_known_identifier()
    {
        SystemVariableValueRequestedV1Handler.AutomationOperator.Value
            .ShouldBe(Guid.Parse("a07a07a0-7000-7000-8000-000000000007"));
    }

    [Fact]
    public async Task A_success_forwards_the_acceptance_moment_when_the_request_carries_one()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(NumberVariable("munich", "oeeLine1", 1));

        var run = await RunAsync(
            "oeeLine1", "82.5", Causing, MetadataFor("munich", rootIngestedAt: Accepted), repo: repo);

        var change = run.Changes.ShouldHaveSingleItem();
        change.RootIngestedAt.HasValue.ShouldBeTrue();
        change.RootIngestedAt.Value.ShouldBe(Accepted);
    }

    [Fact]
    public async Task A_success_forwards_no_acceptance_moment_when_the_request_carries_none()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(NumberVariable("munich", "oeeLine1", 1));

        var run = await RunAsync("oeeLine1", "82.5", Causing, MetadataFor("munich"), repo: repo);

        run.Changes.ShouldHaveSingleItem().RootIngestedAt.HasValue.ShouldBeFalse();
    }

    private static EventMetadata MetadataFor(string? fab, DateTimeOffset? rootIngestedAt = null) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000bb"),
        Requested,
        fab,
        null,
        rootIngestedAt);

    private static Variable NumberVariable(string fab, string name, double value) =>
        new VariableBuilder()
            .WithFab(fab)
            .Named(name)
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(value))
            .At(Moment)
            .Build();

    private static async Task<Run> RunAsync(
        string name,
        string value,
        Guid causing,
        EventMetadata? metadata,
        bool reserves = true,
        InMemoryVariableRepository? repo = null)
    {
        repo ??= new InMemoryVariableRepository();
        List<VariableValueChangedDomainEvent> changes = [];
        repo.OnDomainEvent = (domainEvent, _) =>
        {
            if (domainEvent is VariableValueChangedDomainEvent changed)
            {
                changes.Add(changed);
            }

            return Task.CompletedTask;
        };
        RecordingDedupStore dedup = new(reserves);
        CapturingLogger<SystemVariableValueRequestedV1Handler> logger = new();
        SystemVariableValueRequestedV1Handler handler = new(
            dedup,
            new SetVariableValueCommandHandler(
                repo,
                new FakeClock(Moment),
                NullLogger<SetVariableValueCommandHandler>.Instance),
            logger);

        await handler.Handle(
            new SystemVariableValueRequestedV1(name, value, Requested, causing, metadata!),
            CancellationToken.None);

        return new Run(dedup, logger, changes);
    }

    private sealed record Run(
        RecordingDedupStore Dedup,
        CapturingLogger<SystemVariableValueRequestedV1Handler> Logger,
        IReadOnlyList<VariableValueChangedDomainEvent> Changes);

    private sealed class RecordingDedupStore(bool reserves) : IVariableValueRequestDedupStore
    {
        public List<string> Calls { get; } = [];

        public Task<bool> TryReserveAsync(
            FabIdentifier fab,
            string variableName,
            Guid causingEventIdentifier,
            CancellationToken cancellationToken)
        {
            Calls.Add($"{fab.Value}|{variableName}|{causingEventIdentifier}");
            return Task.FromResult(reserves);
        }
    }
}
