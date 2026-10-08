using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Ingress;

/// <summary>
/// Phase 4a (spec 269 T003c), extended by T006 (spec 317, #2325).
///
/// <para>
/// <b><see cref="A_discovery_source_holds_an_unregistered_kind"/> is an
/// inversion, named in plan.md §9's sanctioned-edit table.</b> It replaces
/// <c>A_discovery_source_admits_an_unregistered_kind</c>, whose own doc
/// comment recorded that a declared-discovery pair was "indistinguishable
/// from an absent one at this collaborator's read port" — the user's Q1
/// decision (spec 317 §0.2) makes that no longer true: an undeclared pair
/// still admits, but a <em>declared</em> discovery pair now holds.
/// </para>
/// </summary>
public class EventTypeAdmissionTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    private static EventEnvelope BuildEnvelope(string fab = "dresden", Source? source = null, string kind = "PlcCycleStart") =>
        new(
            EventIdentifier.New(),
            FabIdentifier.From(fab),
            source ?? Source.Manual,
            DeviceIdentifier.From("station-4"),
            Kind.From(kind),
            OccurredAt.From(Now),
            Payload.From("{}"));

    /// <summary>Unchanged characterisation (Q1, option A): the undeclared default stays open.</summary>
    [Fact]
    public async Task An_undeclared_source_admits_an_unregistered_kind()
    {
        InMemoryEventTypeAdmissionSource source = new();
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeFalse();
        verdicts.Holds(envelope).ShouldBeFalse();
    }

    /// <summary>
    /// T006 (spec 317, #2325) — FR-005. Inverts
    /// <c>A_discovery_source_admits_an_unregistered_kind</c> (plan.md §9):
    /// under the new quarantine behaviour a <em>declared</em> discovery pair
    /// holds an unregistered kind rather than admitting it.
    /// </summary>
    [Fact]
    public async Task A_discovery_source_holds_an_unregistered_kind()
    {
        FabIdentifier berlin = FabIdentifier.From("berlin");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareDiscovery(berlin, Source.Manual);
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "berlin", kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Holds(envelope).ShouldBeTrue();
        verdicts.Refuses(envelope).ShouldBeFalse("a hold is not a refusal — the two are distinct outcomes");
    }

    /// <summary>T006 — a declared discovery pair does not hold a kind that is registered.</summary>
    [Fact]
    public async Task A_discovery_source_admits_a_registered_kind()
    {
        FabIdentifier berlin = FabIdentifier.From("berlin");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareDiscovery(berlin, Source.Manual);
        source.Register(RegisteredEventType.Register(
            berlin, Kind.From("PlcCycleStart"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now)));
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "berlin", kind: "PlcCycleStart");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Holds(envelope).ShouldBeFalse();
        verdicts.Refuses(envelope).ShouldBeFalse();
    }

    /// <summary>T006 — a retired kind is held under discovery, same as an unregistered one.</summary>
    [Fact]
    public async Task A_retired_kind_is_held_under_discovery()
    {
        FabIdentifier berlin = FabIdentifier.From("berlin");
        RegisteredEventType retired = RegisteredEventType.Register(
            berlin, Kind.From("WasOnceRegistered"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        retired.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareDiscovery(berlin, Source.Manual);
        source.Register(retired);
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "berlin", kind: "WasOnceRegistered");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Holds(envelope).ShouldBeTrue();
    }

    [Fact]
    public async Task A_strict_source_refuses_an_unregistered_kind()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Manual);
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "dresden", source: Source.Manual, kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeTrue();
    }

    /// <summary>T006 — strict and discovery are distinct outcomes: a refusal is never also a hold.</summary>
    [Fact]
    public async Task A_strict_source_does_not_hold_the_kind_it_refuses()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Manual);
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "dresden", source: Source.Manual, kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Holds(envelope).ShouldBeFalse();
    }

    [Fact]
    public async Task A_strict_source_admits_a_registered_kind()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Manual);
        source.Register(RegisteredEventType.Register(
            dresden, Kind.From("PlcCycleStart"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now)));
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "dresden", source: Source.Manual, kind: "PlcCycleStart");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeFalse();
    }

    [Fact]
    public async Task Strict_on_one_source_leaves_the_fabs_other_sources_open()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Inference);
        EventTypeAdmission admission = new(source);
        EventEnvelope onPlc = BuildEnvelope(fab: "dresden", source: Source.Plc, kind: "Unregistered");

        EventTypeVerdicts verdicts = await admission.AssessAsync([onPlc], CancellationToken.None);

        verdicts.Refuses(onPlc).ShouldBeFalse();
    }

    [Fact]
    public async Task Strict_in_one_fab_leaves_the_same_source_in_another_fab_open()
    {
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(FabIdentifier.From("dresden"), Source.Manual);
        EventTypeAdmission admission = new(source);
        EventEnvelope inMunich = BuildEnvelope(fab: "munich", source: Source.Manual, kind: "Unregistered");

        EventTypeVerdicts verdicts = await admission.AssessAsync([inMunich], CancellationToken.None);

        verdicts.Refuses(inMunich).ShouldBeFalse();
    }

    [Fact]
    public async Task A_retired_kind_is_refused_under_strict()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        RegisteredEventType retired = RegisteredEventType.Register(
            dresden, Kind.From("WasOnceRegistered"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        retired.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Manual);
        source.Register(retired);
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "dresden", source: Source.Manual, kind: "WasOnceRegistered");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeTrue();
    }

    [Fact]
    public async Task The_default_case_costs_one_query()
    {
        InMemoryEventTypeAdmissionSource source = new();
        EventTypeAdmission admission = new(source);
        EventEnvelope[] envelopes = [.. Enumerable.Range(0, 50).Select(_ => BuildEnvelope())];

        await admission.AssessAsync(envelopes, CancellationToken.None);

        source.DeclaredSourceModesCalls.ShouldBe(1);
        source.RegisteredKindsCalls.ShouldBe(0, "no declared pairs are present, so no registry lookup is owed");
    }

    [Fact]
    public async Task A_batch_costs_one_registry_query_per_strict_fab_not_per_envelope()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        FabIdentifier munich = FabIdentifier.From("munich");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(dresden, Source.Manual);
        source.DeclareStrict(munich, Source.Manual);
        EventTypeAdmission admission = new(source);

        List<EventEnvelope> envelopes = [];
        for (int i = 0; i < 200; i++)
        {
            string fab = i % 2 == 0 ? "dresden" : "munich";
            envelopes.Add(BuildEnvelope(fab: fab, source: Source.Manual, kind: $"Kind{i % 5}"));
        }

        await admission.AssessAsync(envelopes, CancellationToken.None);

        source.RegisteredKindsCalls.ShouldBe(
            2, "one call per declared fab in the batch, not per envelope and not per distinct kind");
    }

    /// <summary>T006 — a declared discovery fab costs a registry query too, same as strict.</summary>
    [Fact]
    public async Task A_batch_costs_one_registry_query_per_discovery_fab_not_per_envelope()
    {
        FabIdentifier berlin = FabIdentifier.From("berlin");
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareDiscovery(berlin, Source.Manual);
        EventTypeAdmission admission = new(source);
        EventEnvelope[] envelopes = [.. Enumerable.Range(0, 20).Select(i => BuildEnvelope(fab: "berlin", kind: $"Kind{i % 3}"))];

        await admission.AssessAsync(envelopes, CancellationToken.None);

        source.RegisteredKindsCalls.ShouldBe(1, "one call for the one declared fab in this batch, not per envelope");
    }

    /// <summary>
    /// Green on arrival, but not for the reason plan.md §6.1 step 1 describes
    /// — see this class's remarks.
    /// </summary>
    [Fact]
    public async Task An_empty_batch_queries_nothing()
    {
        InMemoryEventTypeAdmissionSource source = new();
        EventTypeAdmission admission = new(source);

        EventTypeVerdicts verdicts = await admission.AssessAsync([], CancellationToken.None);

        source.DeclaredSourceModesCalls.ShouldBe(0);
        source.RegisteredKindsCalls.ShouldBe(0);
        verdicts.ShouldBeSameAs(EventTypeVerdicts.AdmitAll);
    }
}
