using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedV3HandlerTests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Upserts_the_overlay_into_the_reverse_index()
    {
        InMemoryReverseIndex index = new();
        OverlayRevisionPublishedV3Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV3Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV3 message = new(
            overlay, 1, "Line",
            [new OverlayElementV3("Text", "#FFFFFFD9", 0.1m, 0.1m, 0.3m, 0.08m, "OEE: {{oee}}%", 32)],
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message);

        index.LookupLabelTexts(overlay).ShouldHaveSingleItem().ShouldBe("OEE: {{oee}}%");
        index.LookupOverlays("oee").ShouldBe(new[] { overlay });
    }

    [Fact]
    public async Task Upserts_every_element_and_registers_the_union_of_their_placeholders()
    {
        InMemoryReverseIndex index = new();
        OverlayRevisionPublishedV3Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV3Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV3 message = new(
            overlay, 1, "Line",
            [
                new OverlayElementV3("Text", "#FFFFFFD9", 0.1m, 0.1m, 0.3m, 0.08m, "OEE: {{oee}}%", 32),
                new OverlayElementV3("Text", "#FFFFFFD9", 0.2m, 0.2m, 0.3m, 0.08m, "Status: {{status}}", 24),
            ],
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message);

        IReadOnlyList<string>? texts = index.LookupLabelTexts(overlay);
        texts!.Count.ShouldBe(2);
        texts[0].ShouldBe("OEE: {{oee}}%");
        texts[1].ShouldBe("Status: {{status}}");
        index.LookupOverlays("oee").ShouldBe(new[] { overlay });
        index.LookupOverlays("status").ShouldBe(new[] { overlay });
    }

    /// <summary>
    /// Spec 300 (#2349), ADR-0165, T014: a Box at ordinal 0 contributes ""
    /// at its position, and the Text element at ordinal 1 still resolves —
    /// confirmed by test, not assumed (plan.md §"SystemVariables").
    /// </summary>
    [Fact]
    public async Task A_box_at_ordinal_zero_contributes_an_empty_string_and_the_text_element_still_resolves()
    {
        InMemoryReverseIndex index = new();
        OverlayRevisionPublishedV3Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV3Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV3 message = new(
            overlay, 1, "Line",
            [
                new OverlayElementV3("Box", "#D32F2F00", 0.0m, 0.0m, 0.2m, 0.2m, null, null),
                new OverlayElementV3("Text", "#FFFFFFD9", 0.2m, 0.2m, 0.3m, 0.08m, "OEE: {{oee}}%", 32),
            ],
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message);

        IReadOnlyList<string>? texts = index.LookupLabelTexts(overlay);
        texts!.Count.ShouldBe(2);
        texts[0].ShouldBe(string.Empty);
        texts[1].ShouldBe("OEE: {{oee}}%");
        index.LookupOverlays("oee").ShouldBe(new[] { overlay });
    }
}
