using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class OverlayRevisionArchivedV1HandlerTests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Removes_the_overlay_from_the_reverse_index()
    {
        InMemoryReverseIndex index = new();
        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oee}}%");
        index.LookupOverlays("oee").ShouldHaveSingleItem();

        OverlayRevisionArchivedV1Handler handler = new(
            index, NullLogger<OverlayRevisionArchivedV1Handler>.Instance);
        await handler.Handle(new OverlayRevisionArchivedV1(
            overlay, 1, DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata));

        index.LookupLabelText(overlay).ShouldBeNull();
        index.LookupOverlays("oee").ShouldBeEmpty();
    }
}
