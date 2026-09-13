using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Stands in for <c>RegisteredEventTypeRepository</c> plus
/// <c>AggregateVersionInterceptor</c>, mirroring
/// <c>InMemoryWebhookIntegrationRepository</c> (ADR-0054, no mocking
/// framework).
/// </summary>
public sealed class InMemoryRegisteredEventTypeRepository : IRegisteredEventTypeRepository
{
    private readonly List<RegisteredEventType> _eventTypes = [];
    private readonly HashSet<Guid> _persisted = [];

    public IReadOnlyList<RegisteredEventType> EventTypes => _eventTypes;

    /// <summary>
    /// Places an entry that already exists in the database, at
    /// <paramref name="version"/>. Distinct from <see cref="Add"/>, which is
    /// the production path for a row being created now.
    /// </summary>
    public void Seed(RegisteredEventType eventType, int version = 0)
    {
        Ensure.That(eventType).IsNotNull();

        AggregateVersions.SetTo(eventType, version);

        _eventTypes.Add(eventType);
        _persisted.Add(eventType.Id.Value);
        eventType.ClearPendingEvents();
    }

    public Task<Option<RegisteredEventType>> GetRegisteredAsync(
        FabIdentifier fab, Kind kind, CancellationToken cancellationToken)
    {
        RegisteredEventType? found = _eventTypes.SingleOrDefault(
            eventType => eventType.Fab == fab && eventType.Kind == kind
                && eventType.State == RegistrationState.Registered);

        return Task.FromResult(found is null
            ? Option<RegisteredEventType>.None
            : Option<RegisteredEventType>.Some(found));
    }

    public Task<Option<RegisteredEventType>> GetRegisteredAsync(
        IReadOnlyList<FabIdentifier> fabs, Kind kind, CancellationToken cancellationToken)
    {
        RegisteredEventType? found = _eventTypes.SingleOrDefault(
            eventType => fabs.Contains(eventType.Fab) && eventType.Kind == kind
                && eventType.State == RegistrationState.Registered);

        return Task.FromResult(found is null
            ? Option<RegisteredEventType>.None
            : Option<RegisteredEventType>.Some(found));
    }

    public void Add(RegisteredEventType eventType)
    {
        Ensure.That(eventType).IsNotNull();
        _eventTypes.Add(eventType);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (RegisteredEventType eventType in _eventTypes)
        {
            bool wasAlreadyPersisted = !_persisted.Add(eventType.Id.Value);
            if (wasAlreadyPersisted && eventType.PendingEvents.Count > 0)
            {
                AggregateVersions.Bump(eventType);
            }

            eventType.ClearPendingEvents();
        }

        return Task.CompletedTask;
    }
}
