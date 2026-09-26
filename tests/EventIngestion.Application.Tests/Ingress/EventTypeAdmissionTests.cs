using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Ingress;

/// <summary>
/// Phase 4a (spec 269 T003c). Exercises <see cref="EventTypeAdmission"/>
/// against the signature T002 introduced with its behaviour withheld —
/// <c>AssessAsync</c> always answers <see cref="EventTypeVerdicts.AdmitAll"/>
/// without calling <see cref="IEventTypeAdmissionSource"/> at all.
///
/// <para>
/// Not every case here is red on arrival. <see cref="An_undeclared_source_admits_an_unregistered_kind"/>
/// and <see cref="A_discovery_source_admits_an_unregistered_kind"/> are
/// tasks.md's own documented green cases (T003c.1–2) — they are also the
/// characterisation cases (spec.md's default scenario). <b>Undocumented by
/// tasks.md, but also green on arrival</b>, because
/// <c>EventTypeVerdicts.Refuses</c> always answers <c>false</c>: every case
/// that asserts <c>ShouldBeFalse()</c> passes regardless of what is
/// declared — <see cref="A_strict_source_admits_a_registered_kind"/>,
/// <see cref="Strict_on_one_source_leaves_the_fabs_other_sources_open"/>,
/// <see cref="Strict_in_one_fab_leaves_the_same_source_in_another_fab_open"/>
/// and <see cref="An_empty_batch_queries_nothing"/> (the last because T002's
/// stub never calls the source regardless of input, empty or not — not
/// because the empty-input short-circuit of plan.md §6.1 step 1 exists yet).
/// Only the cases that assert <c>ShouldBeTrue()</c> or a call count are
/// genuinely red. See the phase 4a report for the full, verified list.
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

    /// <summary>Green on arrival (spec 269 tasks.md T003c.1) — this is the characterisation case.</summary>
    [Fact]
    public async Task An_undeclared_source_admits_an_unregistered_kind()
    {
        InMemoryEventTypeAdmissionSource source = new();
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeFalse();
    }

    /// <summary>
    /// Green on arrival (spec 269 tasks.md T003c.2). An explicit discovery
    /// declaration is indistinguishable from an absent one at this
    /// collaborator's read port — <c>StrictSourcesAsync</c> only ever names
    /// strict pairs (spec.md's "discovery declared explicitly" scenario).
    /// </summary>
    [Fact]
    public async Task A_discovery_source_admits_an_unregistered_kind()
    {
        InMemoryEventTypeAdmissionSource source = new();
        EventTypeAdmission admission = new(source);
        EventEnvelope envelope = BuildEnvelope(fab: "berlin", kind: "NobodyDeclaredThis");

        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], CancellationToken.None);

        verdicts.Refuses(envelope).ShouldBeFalse();
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

        source.StrictSourcesCalls.ShouldBe(1);
        source.RegisteredKindsCalls.ShouldBe(0, "no strict pairs are present, so no registry lookup is owed");
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
            2, "one call per strict fab in the batch, not per envelope and not per distinct kind");
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

        source.StrictSourcesCalls.ShouldBe(0);
        source.RegisteredKindsCalls.ShouldBe(0);
        verdicts.ShouldBeSameAs(EventTypeVerdicts.AdmitAll);
    }
}
