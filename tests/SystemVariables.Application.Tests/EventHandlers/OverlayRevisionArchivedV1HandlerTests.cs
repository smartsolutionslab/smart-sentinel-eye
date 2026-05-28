using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class OverlayRevisionArchivedV1HandlerTests
{
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
            overlay, 1, DateTimeOffset.UtcNow, Guid.CreateVersion7()));

        index.LookupLabelText(overlay).ShouldBeNull();
        index.LookupOverlays("oee").ShouldBeEmpty();
    }
}
