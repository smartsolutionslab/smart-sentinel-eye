using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// Lists events with optional source / device / kind / time-range
/// filters; cursor-paginated by <c>(ingestedAt, eventId)</c>
/// (spec 006 FR-018). Default page size 100, max 1 000.
/// </summary>
/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 018 FR-001, FR-003). A read
/// spans all of them when none is named — the deliberate asymmetry with the
/// write path, which must choose.
/// </summary>
public sealed record ListEventsQuery(
    IReadOnlyList<FabIdentifier> Fabs,
    Source? Source,
    DeviceIdentifier? Device,
    Kind? Kind,
    DateTimeOffset? OccurredAfter,
    DateTimeOffset? OccurredBefore,
    DateTimeOffset? IngestedAfter,
    DateTimeOffset? IngestedBefore,
    int PageSize,
    string? Cursor)
    : IQuery<Result<EventPageDto, ListEventsError>>;
