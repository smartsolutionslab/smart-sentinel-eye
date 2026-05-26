using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Reconciles a Stream's stored state with the SFU's observed health.
/// Dispatched by <c>StreamHealthWatcher</c> (Infrastructure) every poll
/// when the observation would cause a transition (FR-008 / FR-009).
/// </summary>
public sealed record ReportStreamHealthCommand(
    CameraIdentifier Camera,
    RtspPathHealth Observation,
    bool DeclareOffline)
    : ICommand<Result<StreamState, ReportStreamHealthError>>;
