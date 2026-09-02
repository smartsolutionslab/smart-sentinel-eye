using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Provisions a looping-video path on <c>camera-sim</c> — the second,
/// config-clean MediaMTX (ADR-0111). For each catalog camera, adds a path whose
/// <c>runOnDemand</c> hook spawns FFmpeg to loop the generated clip and publish
/// it back as RTSP, so the path is only running while something pulls it (the
/// main MediaMTX, on a WHEP open). This is the catalog -> sim sync, triggered by
/// the <c>CameraRegisteredV1</c> integration event.
/// </summary>
public sealed class CameraSimProvisioner(HttpClient http, ILogger<CameraSimProvisioner> logger)
{
    /// <summary>The clip every camera played before an asset could name its own.</summary>
    public const string DefaultClip = "sim-loop.mp4";

    // -stream_loop -1 loops forever; -re paces at real time so the loop plays
    // at 1x; -c copy avoids a re-encode (the clips are already H.264). $MTX_PATH
    // is substituted by MediaMTX with the path name at runtime.
    private static string LoopCommand(string clip) =>
        $"ffmpeg -stream_loop -1 -re -i /media/{clip} -c copy -f rtsp rtsp://localhost:8554/$MTX_PATH";

    /// <summary>
    /// The command for a camera belonging to no scenario asset: the shared clip
    /// with the camera's name drawn on it and a hue rotation derived from its
    /// identifier, so two such cameras never look alike (FR-004).
    /// </summary>
    /// <remarks>
    /// This one <b>re-encodes</b>. <c>-c copy</c> exists precisely so FFmpeg does
    /// no per-stream work, and a burnt-in label cannot survive a stream copy — the
    /// pixels have to change. That is affordable only because FR-010 sets the dev
    /// target at ~20 cameras. Point 250 at this and the dev box is where you find
    /// out; the number is the assumption, not the encoder settings.
    /// </remarks>
    /// <summary>
    /// The font `drawtext` renders with, inside camera-sim's <c>/media</c> mount.
    /// </summary>
    /// <remarks>
    /// <b>Named explicitly because the image has none.</b> `bluenviron/mediamtx`
    /// ships no fonts at all — no <c>/usr/share/fonts</c>, no <c>.ttf</c>
    /// anywhere — so a bare <c>drawtext</c> fails with "Cannot find a valid font
    /// for the family Sans", the ffmpeg process never starts, and the path never
    /// becomes ready. The camera then shows nothing, which on a wall is
    /// indistinguishable from a broken camera.
    /// <para>
    /// The unit tests could not catch that: they assert the shape of the command
    /// string and never run ffmpeg. It was found by executing the command against
    /// the real image.
    /// </para>
    /// </remarks>
    private const string FontFile = "/media/DejaVuSans.ttf";

    private static string LabelledCommand(string clip, string label, int hueDegrees) =>
        $"ffmpeg -stream_loop -1 -re -i /media/{clip} " +
        $"-vf \"hue=h={hueDegrees},drawtext=fontfile={FontFile}:text='{Sanitize(label)}':" +
        "x=24:y=24:fontsize=36:fontcolor=white:box=1:boxcolor=black@0.5:boxborderw=10\" " +
        "-c:v libx264 -preset veryfast -tune zerolatency -pix_fmt yuv420p " +
        "-f rtsp rtsp://localhost:8554/$MTX_PATH";

    /// <summary>
    /// Strips what would break out of the drawtext argument. The label is a camera
    /// name an operator typed, and it lands inside a quoted shell-ish expression
    /// MediaMTX hands to FFmpeg — a trust boundary, so it is filtered here rather
    /// than hoped about.
    /// </summary>
    private static string Sanitize(string label) =>
        new(label.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').Take(48).ToArray());

    public Task ProvisionLoopPathAsync(string path, string clip, CancellationToken cancellationToken)
    {
        Ensure.That(clip).IsNotNull().IsNotNullOrWhiteSpace();
        return ProvisionAsync(path, clip, LoopCommand(clip), cancellationToken);
    }

    /// <summary>
    /// Provisions a camera that belongs to no scenario asset: the shared clip,
    /// labelled with <paramref name="label"/> and hue-shifted by its identifier so
    /// two of them are never the same picture (FR-004).
    /// </summary>
    public async Task ProvisionLabelledPathAsync(
        string path,
        string label,
        Guid camera,
        CancellationToken cancellationToken)
    {
        Ensure.That(label).IsNotNull().IsNotNullOrWhiteSpace();

        // Deterministic per camera, so a restart does not recolour the wall. The
        // spread is what matters, not the value — 360 buckets off the identifier.
        int hue = (int)((uint)camera.GetHashCode() % 360);

        await ProvisionAsync(path, DefaultClip, LabelledCommand(DefaultClip, label, hue), cancellationToken);

        // Which camera's label the path now carries, because ProvisionAsync can
        // only say it plays `sim-loop.mp4` — true of every labelled path, and so
        // no help at all when two cameras share one.
        //
        // The guarantee is per simulator path, not per camera row: two cameras
        // registered at the same URL are one source, and the later registration
        // wins the label and the hue. That is the line to look for when a
        // camera's picture changes without anyone touching that camera.
        logger.CameraSimPathLabelled(path, label, camera);
    }

    private async Task ProvisionAsync(
        string path,
        string clip,
        string command,
        CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull().IsNotNullOrWhiteSpace();

        PathConfig body = new(
            RunOnDemand: command,
            RunOnDemandRestart: true,
            RunOnDemandCloseAfter: "10s");

        using HttpResponseMessage response = await http
            .PostAsJsonAsync($"/v3/config/paths/add/{path}", body, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            logger.CameraSimPathProvisioned(path, clip);
            return;
        }

        // MediaMTX returns 400 for a duplicate add. Returning here — which is what
        // this did — is right for an unchanged path and wrong for a changed one:
        // edit a scenario's clip, restart, and the old picture keeps playing with
        // no error anywhere. The symptom is the absence of one, so the cause gets
        // looked for in config binding.
        //
        // Replace instead. It is idempotent for the unchanged case and correct for
        // the changed one, which makes the distinction stop mattering.
        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!detail.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            response.EnsureSuccessStatusCode();
        }

        using HttpResponseMessage replaced = await http
            .PostAsJsonAsync($"/v3/config/paths/replace/{path}", body, cancellationToken);

        replaced.EnsureSuccessStatusCode();
        logger.CameraSimPathReplaced(path, clip);
    }

    // MediaMTX v3 path config (subset). runOnDemandRestart re-spawns FFmpeg if
    // it exits; runOnDemandCloseAfter stops it shortly after the last reader.
    private sealed record PathConfig(string RunOnDemand, bool RunOnDemandRestart, string RunOnDemandCloseAfter);
}
