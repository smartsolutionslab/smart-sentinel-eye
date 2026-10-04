using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedV2HandlerTests
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
        OverlayRevisionPublishedV2Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV2Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV2 message = new(
            overlay, 1, "Line",
            [new OverlayLabelV2("OEE: {{oee}}%", 0.1m, 0.1m, 0.3m, 0.08m, 32)],
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message);

        index.LookupLabelTexts(overlay).ShouldHaveSingleItem().ShouldBe("OEE: {{oee}}%");
        index.LookupOverlays("oee").ShouldBe(new[] { overlay });
    }

    [Fact]
    public async Task Upserts_every_label_and_registers_the_union_of_their_placeholders()
    {
        InMemoryReverseIndex index = new();
        OverlayRevisionPublishedV2Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV2Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV2 message = new(
            overlay, 1, "Line",
            [
                new OverlayLabelV2("OEE: {{oee}}%", 0.1m, 0.1m, 0.3m, 0.08m, 32),
                new OverlayLabelV2("Status: {{status}}", 0.2m, 0.2m, 0.3m, 0.08m, 24),
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
}
