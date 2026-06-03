using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Seeding;

/// <summary>
/// DEV-ONLY seeder that registers a handful of simulated cameras so a plain
/// <c>aspire run</c> shows live tiles with no real hardware. Each camera's
/// <see cref="RtspUrl"/> points at a static <c>sim-cam-N</c> path on MediaMTX
/// whose on-demand FFmpeg publishes a labelled H.264 test pattern
/// (Resources/mediamtx.yml). Registration flows through the normal
/// <see cref="RegisterCameraCommand"/>, so the existing CameraRegistered ->
/// ProvisionStream -> AddPath pipeline wires the MediaMTX <c>cam-{guid}</c>
/// path to the simulated source automatically.
///
/// <para>
/// Wired only when AppHost sets <c>CameraCatalog:SeedSimulatedCameras</c>
/// (dev run mode, never under E2ETests/CI). Idempotent: the command handler
/// rejects duplicate names, so a re-run after a persisted volume restart is a
/// no-op. Best-effort: a DB/registration failure is logged but does not crash
/// the host.
/// </para>
/// </summary>
public sealed class SimulatedCameraSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<SimulatedCameraSeeder> logger) : IHostedService
{
    // Stable synthetic operator for dev-seeded rows; only needs to be a
    // non-empty Guid (OperatorIdentifier records who registered the camera).
    private static readonly OperatorIdentifier SeedOperator =
        OperatorIdentifier.From(Guid.Parse("01920000-0000-7000-8000-0000000005ED"));

    private static readonly IReadOnlyList<(string Name, string RtspUrl)> SimulatedCameras =
    [
        ("Sim Cam 1 - Station 4", "rtsp://mediamtx:8554/sim-cam-1"),
        ("Sim Cam 2 - Loading Bay", "rtsp://mediamtx:8554/sim-cam-2"),
        ("Sim Cam 3 - Line A", "rtsp://mediamtx:8554/sim-cam-3"),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        RegisterCameraCommandHandler handler =
            scope.ServiceProvider.GetRequiredService<RegisterCameraCommandHandler>();

        int seeded = 0;
        foreach ((string name, string rtspUrl) in SimulatedCameras)
        {
            try
            {
                RegisterCameraCommand command = new(
                    CameraName.From(name),
                    RtspUrl.From(rtspUrl),
                    SeedOperator);

                Result<CameraIdentifier, RegisterCameraError> result =
                    await handler.HandleAsync(command, cancellationToken);

                if (result.IsSuccess)
                {
                    seeded++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A startup-time DB outage must not block the host. The next
                // process restart retries; duplicate-name rejections are
                // already handled inside the command handler (idempotent).
                logger.SimulatedCameraSeedingFailed(ex, name);
            }
        }

        logger.SeededSimulatedCameras(seeded, SimulatedCameras.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
