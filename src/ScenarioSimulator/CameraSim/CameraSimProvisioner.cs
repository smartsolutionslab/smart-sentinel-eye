using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Configuration;

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
    // -stream_loop -1 loops forever; -re paces at real time so the loop plays
    // at 1x; -c copy avoids a re-encode (the clip is already H.264). $MTX_PATH
    // is substituted by MediaMTX with the path name at runtime.
    private const string RunOnDemandCommand =
        "ffmpeg -stream_loop -1 -re -i /media/sim-loop.mp4 -c copy -f rtsp rtsp://localhost:8554/$MTX_PATH";

    public async Task ProvisionLoopPathAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        PathConfig body = new(
            RunOnDemand: RunOnDemandCommand,
            RunOnDemandRestart: true,
            RunOnDemandCloseAfter: "10s");

        using HttpResponseMessage response = await http
            .PostAsJsonAsync($"/v3/config/paths/add/{path}", body, cancellationToken);

        // 400 with "path already exists" is fine on a worker restart — the sim
        // already has the loop. MediaMTX returns 400 for a duplicate add.
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                logger.CameraSimPathAlreadyExists(path);
                return;
            }
            response.EnsureSuccessStatusCode();
        }

        logger.CameraSimPathProvisioned(path);
    }

    // MediaMTX v3 path config (subset). runOnDemandRestart re-spawns FFmpeg if
    // it exits; runOnDemandCloseAfter stops it shortly after the last reader.
    private sealed record PathConfig(string RunOnDemand, bool RunOnDemandRestart, string RunOnDemandCloseAfter);
}
