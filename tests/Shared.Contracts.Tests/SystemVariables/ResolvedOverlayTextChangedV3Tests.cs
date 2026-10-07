using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests.SystemVariables;

/// <summary>
/// The V3 contract shape: <c>Texts</c> carries template-keyed pairs
/// (<c>ResolvedOverlayTextV3</c>, each a <c>(Template, Resolved)</c>
/// pair), not a positional list, so a consumer pairs by template and
/// never by index. V2 (the positional-list contract) has been removed
/// entirely — there is no production type or test file for it left to
/// mirror.
/// </summary>
public class ResolvedOverlayTextChangedV3Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        IReadOnlyList<ResolvedOverlayTextV3> texts =
            [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%"), new ResolvedOverlayTextV3("{{shift}}", "Line 1")];
        ResolvedOverlayTextChangedV3 evt = new(overlay, texts, 7, Metadata: TestMetadata);

        evt.Overlay.ShouldBe(overlay);
        evt.Texts.ShouldBe(texts);
        evt.Version.ShouldBe(7);
    }

    [Fact]
    public void Each_pair_carries_its_own_template_and_resolved_value()
    {
        ResolvedOverlayTextV3 pair = new("OEE: {{oee}}%", "OEE: 82.5%");

        pair.Template.ShouldBe("OEE: {{oee}}%");
        pair.Resolved.ShouldBe("OEE: 82.5%");
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        ResolvedOverlayTextChangedV3 evt = new(Guid.CreateVersion7(), [new ResolvedOverlayTextV3("{{x}}", "x")], 1, Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        IReadOnlyList<ResolvedOverlayTextV3> texts = [new ResolvedOverlayTextV3("{{v}}", "v")];
        ResolvedOverlayTextChangedV3 a = new(overlay, texts, 3, Metadata: TestMetadata);
        ResolvedOverlayTextChangedV3 b = new(overlay, texts, 3, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void A_single_version_bump_covers_the_whole_resolved_set()
    {
        IReadOnlyList<ResolvedOverlayTextV3> texts =
            [new ResolvedOverlayTextV3("{{a}}", "First"), new ResolvedOverlayTextV3("{{b}}", "Second"), new ResolvedOverlayTextV3("{{c}}", "Third")];
        ResolvedOverlayTextChangedV3 evt = new(Guid.CreateVersion7(), texts, 9, Metadata: TestMetadata);

        evt.Texts.Count.ShouldBe(3);
        evt.Version.ShouldBe(9);
    }

    [Fact]
    public void JSON_round_trip_preserves_every_pair_in_order()
    {
        ResolvedOverlayTextChangedV3 original =
            new(Guid.CreateVersion7(), [new ResolvedOverlayTextV3("OEE: {{oee}}%", "OEE: 82.5%"), new ResolvedOverlayTextV3("{{shift}}", "Line 1")], 9, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        ResolvedOverlayTextChangedV3 deserialized =
            JsonSerializer.Deserialize<ResolvedOverlayTextChangedV3>(json)!;

        deserialized.Overlay.ShouldBe(original.Overlay);
        deserialized.Version.ShouldBe(original.Version);
        deserialized.Texts.Count.ShouldBe(2);
        deserialized.Texts[0].Template.ShouldBe("OEE: {{oee}}%");
        deserialized.Texts[0].Resolved.ShouldBe("OEE: 82.5%");
        deserialized.Texts[1].Template.ShouldBe("{{shift}}");
        deserialized.Texts[1].Resolved.ShouldBe("Line 1");
    }

    /// <summary>
    /// The other half of the clean cut (plan.md "Why no ADR" item 3; human
    /// sign-off recorded on #2720): V2 is deleted in the same commit as V3
    /// lands, not kept around for a deprecation window. Checked by
    /// reflection over the assembly rather than a missing-type compile
    /// error, so the fact survives as a runtime check once V3 exists and V2
    /// is gone — on today's code it fails because V2 is still present.
    /// </summary>
    [Fact]
    public void ResolvedOverlayTextChangedV2_no_longer_exists_in_the_assembly()
    {
        Assembly assembly = typeof(ResolvedOverlayTextChangedV3).Assembly;

        assembly.GetType("SmartSentinelEye.Shared.Contracts.SystemVariables.ResolvedOverlayTextChangedV2")
            .ShouldBeNull("V2 is deleted in the same commit as the V3 cut — no dual-publish window (plan.md 'Why no ADR' item 3)");
    }
}
