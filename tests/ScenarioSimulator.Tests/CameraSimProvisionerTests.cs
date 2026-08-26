using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SmartSentinelEye.ScenarioSimulator.CameraSim;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 044 — the provisioner is where "every camera plays the same file" lived,
/// so it is where the guard against it going back belongs.
/// </summary>
public sealed class CameraSimProvisionerTests
{
    [Fact]
    public async Task The_provisioned_command_plays_the_clip_the_asset_named()
    {
        RecordingHandler handler = new();
        CameraSimProvisioner provisioner = Build(handler);

        await provisioner.ProvisionLoopPathAsync("paper-press-group", "paper-press-group.mp4", CancellationToken.None);

        // The assertion that fails if someone restores the constant. Naming the
        // old file explicitly matters: a command containing both would satisfy a
        // "contains the clip" check while still playing the shared loop.
        handler.LastBody.ShouldContain("/media/paper-press-group.mp4");
        handler.LastBody.ShouldNotContain("sim-loop.mp4");
    }

    [Fact]
    public async Task Two_assets_with_different_clips_get_different_commands()
    {
        RecordingHandler handler = new();
        CameraSimProvisioner provisioner = Build(handler);

        await provisioner.ProvisionLoopPathAsync("a", "one.mp4", CancellationToken.None);
        string first = handler.LastBody;
        await provisioner.ProvisionLoopPathAsync("b", "two.mp4", CancellationToken.None);

        first.ShouldNotBe(handler.LastBody);
    }

    /// <summary>
    /// FR-008, and the one whose failure mode is silence: the old code returned
    /// on "already exists", so editing a scenario's clip changed nothing and said
    /// nothing.
    /// </summary>
    [Fact]
    public async Task A_path_that_already_exists_is_replaced_rather_than_left_alone()
    {
        RecordingHandler handler = new()
        {
            FirstResponse = (HttpStatusCode.BadRequest, "path already exists"),
        };
        CameraSimProvisioner provisioner = Build(handler);

        await provisioner.ProvisionLoopPathAsync("station-4", "new-clip.mp4", CancellationToken.None);

        handler.Paths.Count.ShouldBe(2);
        handler.Paths[0].ShouldBe("/v3/config/paths/add/station-4");
        handler.Paths[1].ShouldBe("/v3/config/paths/replace/station-4");
        handler.LastBody.ShouldContain("new-clip.mp4");
    }

    /// <summary>
    /// FR-004. A bulk camera has to be tellable from its neighbour, and the only
    /// thing distinguishing them is what the command draws.
    /// </summary>
    [Fact]
    public async Task A_camera_with_no_asset_gets_its_name_drawn_and_its_own_hue()
    {
        RecordingHandler handler = new();
        CameraSimProvisioner provisioner = Build(handler);

        await provisioner.ProvisionLabelledPathAsync("bulk-a", "Line 7 Spare", Guid.NewGuid(), CancellationToken.None);
        string first = handler.LastBody;
        await provisioner.ProvisionLabelledPathAsync("bulk-b", "Line 8 Spare", Guid.NewGuid(), CancellationToken.None);

        first.ShouldContain("drawtext");
        first.ShouldContain("Line 7 Spare");
        first.ShouldNotBe(handler.LastBody);

        // The font, named explicitly. `bluenviron/mediamtx` ships none, so a bare
        // drawtext fails with "Cannot find a valid font for the family Sans", the
        // ffmpeg process never starts and the path never goes ready — which on a
        // wall looks exactly like a broken camera. That shipped, because a test
        // asserting the command *string* cannot notice a command that will not
        // run; it was found by executing this against the real image.
        first.ShouldContain("fontfile=/media/DejaVuSans.ttf");

        // It cannot stream-copy: the pixels change. Asserted so the cost is a
        // decision someone made rather than a surprise on a dev box.
        first.ShouldContain("libx264");
        first.ShouldNotContain("-c copy");
    }

    /// <summary>
    /// The label is an operator-typed camera name landing inside an FFmpeg
    /// argument that MediaMTX runs — a trust boundary, so it is filtered.
    /// </summary>
    [Fact]
    public async Task A_label_cannot_carry_quotes_or_shell_punctuation_into_the_command()
    {
        RecordingHandler handler = new();
        CameraSimProvisioner provisioner = Build(handler);

        await provisioner.ProvisionLabelledPathAsync(
            "bulk", "evil':x=0,y=0'; rm -rf /", Guid.NewGuid(), CancellationToken.None);

        // The metacharacters, not the words. "rm -rf" surviving as *drawn text*
        // is harmless — it renders on the video and does nothing. What would not
        // be harmless is closing the drawtext argument and starting another, so
        // the quote, the semicolon and the slash are what must be gone.
        string command = handler.LastBody;
        command.ShouldNotContain("'");
        command.ShouldNotContain(";");
        command.ShouldNotContain("/media/sim-loop.mp4 -vf \\u0022hue=h=0,drawtext=text=evil'");

        // And the drawtext option it was trying to inject did not become one.
        command.ShouldNotContain("x=0,y=0");
    }

    private static CameraSimProvisioner Build(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://camera-sim:9997") },
            NullLogger<CameraSimProvisioner>.Instance);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public string LastBody { get; private set; } = string.Empty;

        public (HttpStatusCode Status, string Body)? FirstResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri ?? throw new InvalidOperationException("provisioner sent no URI");
            HttpContent content = request.Content ?? throw new InvalidOperationException("provisioner sent no body");

            Paths.Add(uri.AbsolutePath);
            LastBody = await content.ReadAsStringAsync(cancellationToken);

            if (Paths.Count == 1 && FirstResponse is { } first)
            {
                return new HttpResponseMessage(first.Status) { Content = new StringContent(first.Body) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
