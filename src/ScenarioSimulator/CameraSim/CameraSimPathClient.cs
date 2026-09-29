using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>Whether camera-sim's path is publishing, and when it became ready (FR-004's anchor).</summary>
public sealed record PathState(bool Ready, DateTimeOffset? ReadyTime);

/// <summary>
/// Reads a camera-sim MediaMTX path's readiness (spec 289 FR-004, plan.md §4):
/// a plain <c>GET /v3/paths/get/{path}</c> against the control-plane API.
/// Unauthenticated, the same as <see cref="CameraSimProvisioner"/> — the
/// control plane sits on the docker network, not behind Keycloak. Never
/// throws: every failure — an unreachable host, a non-success status, an
/// unparseable body, an unparseable <c>readyTime</c> — logs and returns
/// <see cref="Option{T}.None"/>.
/// </summary>
public sealed class CameraSimPathClient(HttpClient http, ILogger<CameraSimPathClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Option<PathState>> GetAsync(string path, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"/v3/paths/get/{Uri.EscapeDataString(path)}", cancellationToken);
        }
        catch (Exception exception) when (IsUnexpectedFailure(exception, cancellationToken))
        {
            logger.CameraSimPathUnreadable(path, exception.Message);
            return Option<PathState>.None;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Option<PathState>.None;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.CameraSimPathUnreadable(path, response.StatusCode.ToString());
                return Option<PathState>.None;
            }

            PathResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<PathResponse>(JsonOptions, cancellationToken);
            }
            catch (Exception exception) when (IsUnexpectedFailure(exception, cancellationToken))
            {
                logger.CameraSimPathUnreadable(path, exception.Message);
                return Option<PathState>.None;
            }

            return body is null ? Option<PathState>.None : BuildPathState(path, body);
        }
    }

    /// <summary>
    /// A malformed (non-empty, unparseable) <c>readyTime</c> refuses the
    /// whole reading — <see cref="Option{T}.None"/> — rather than a
    /// <see cref="PathState"/> whose anchor silently reads <c>null</c>: the
    /// caller cannot tell "not ready yet" (a legitimately absent
    /// <c>readyTime</c>) from "camera-sim sent something unusable" unless
    /// this method makes that distinction itself.
    /// </summary>
    private Option<PathState> BuildPathState(string path, PathResponse body)
    {
        if (string.IsNullOrEmpty(body.ReadyTime))
        {
            return Option<PathState>.Some(new PathState(body.Ready, null));
        }

        if (DateTimeOffset.TryParse(
                body.ReadyTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
        {
            return Option<PathState>.Some(new PathState(body.Ready, parsed));
        }

        logger.CameraSimPathUnreadable(path, $"readyTime '{body.ReadyTime}' is not a valid timestamp.");
        return Option<PathState>.None;
    }

    /// <summary>
    /// Whether a caught exception is a genuine failure to log and swallow,
    /// rather than the caller's own cancellation to let propagate. An
    /// <see cref="OperationCanceledException"/> (which
    /// <see cref="TaskCanceledException"/> derives from) is not necessarily
    /// ours: <see cref="HttpClient"/>'s own internal timeout raises one that
    /// is not tied to <paramref name="cancellationToken"/> at all, and that
    /// must be treated as a failure like any other — only a cancellation this
    /// token actually requested is ours to propagate.
    /// </summary>
    private static bool IsUnexpectedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    // Just the two fields FR-004 needs; the rest of MediaMTX's response is not ours to model.
    private sealed record PathResponse(bool Ready, string? ReadyTime);
}
