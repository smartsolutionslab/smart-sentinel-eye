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
public sealed record ListEventsQuery(
    FabIdentifier Fab,
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
