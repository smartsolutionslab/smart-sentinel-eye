using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 408 / #3031 item 2. <c>Uri</c> throws <see cref="IndexOutOfRangeException"/> when reading
/// <c>Host</c> / <c>AbsolutePath</c> of some addresses an older release accepted. The extraction must
/// answer "not a simulated camera" so the handler logs and drops, instead of the exception sending
/// the message through the retry policy to the error queue.
/// </summary>
public sealed class RtspPathUnreadableAddressTests
{
    public static TheoryData<string> UnreadableAddresses =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[1:2:3:4:5:6:7:8:/64]?x",
        "rtsp://[1:2:3:4:5:6:7:8:/64]#x",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "FILE://‎/x",
    ];

    [Theory]
    [MemberData(nameof(UnreadableAddresses))]
    public void An_address_the_parser_cannot_read_is_not_a_simulated_camera_path(string url)
    {
        var extracted = false;
        var path = "unset";

        Should.NotThrow(() => extracted = RtspPath.TryExtract(url, out path));

        extracted.ShouldBeFalse();
        path.ShouldBe(string.Empty);
    }

    [Theory]
    [MemberData(nameof(UnreadableAddresses))]
    public async Task An_address_the_parser_cannot_read_is_skipped_without_a_camera_sim_request(string url)
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await Should.NotThrowAsync(() => rig.HandleAsync(camera, "Unreadable", url));

        rig.CameraSim.Requests.ShouldBeEmpty();
        var entries = rig.Log.Entries.Select(entry => entry.Message).ToList();
        entries.Count.ShouldBe(1);
        entries[0].ShouldContain("<unparseable url>");
        rig.Log.Dump.ShouldNotContain("1:2:3");
        rig.Log.Dump.ShouldNotContain("FILE:");
    }
}
