using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedV1HandlerTests
{
    [Fact]
    public async Task Upserts_the_overlay_into_the_reverse_index()
    {
        InMemoryReverseIndex index = new();
        OverlayRevisionPublishedV1Handler handler = new(
            index, NullLogger<OverlayRevisionPublishedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV1 message = new(
            overlay, 1, "Line", "OEE: {{oee}}%",
            0.1m, 0.1m, 0.3m, 0.08m, 32, DateTimeOffset.UtcNow, Guid.CreateVersion7());

        await handler.Handle(message);

        index.LookupLabelText(overlay).ShouldBe("OEE: {{oee}}%");
        index.LookupOverlays("oee").ShouldBe(new[] { overlay });
    }
}
