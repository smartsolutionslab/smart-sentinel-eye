using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

public sealed record GetOverlayQuery(OverlayIdentifier Overlay)
    : IQuery<Result<OverlayDto, GetOverlayError>>;
