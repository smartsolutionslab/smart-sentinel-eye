using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// Persists an already-validated <see cref="EventEnvelope"/> and
/// publishes <c>FabEventIngestedV1</c>. This is the *single*
/// internal command; the MQTT subscriber and the HTTP endpoints
/// all funnel through it.
///
/// <para>
/// Per-ingress validation (auth, body size, JSON shape) happens at
/// the edge BEFORE this command is dispatched — that's why the
/// payload arrives as the already-constructed <see cref="EventEnvelope"/>.
/// </para>
/// </summary>
public sealed record IngestEventCommand(EventEnvelope Envelope)
    : ICommand<Result<EventIdentifier, IngestEventError>>;
