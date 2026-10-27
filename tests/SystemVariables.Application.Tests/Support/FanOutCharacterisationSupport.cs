using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Support;

/// <summary>
/// An <see cref="IEventBus"/> that keeps the generic type argument each message
/// was published under. The shared <c>FakeEventBus</c> drops it, and the type
/// argument is what the outbox routes on, so a refactor that changed it would
/// pass every test that only looks at the message.
/// </summary>
internal sealed class TypeRecordingEventBus : IEventBus
{
    public List<(Type Type, object Message)> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        Published.Add((typeof(TEvent), integrationEvent));
        return Task.CompletedTask;
    }
}

/// <summary>
/// A reverse index that answers exactly what it was told, in exactly the order
/// it was told, including an overlay it lists but holds no label texts for.
/// </summary>
internal sealed class ListedReverseIndex(
    IReadOnlyList<Guid> overlays,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>?> labelsByOverlay) : IReverseIndex
{
    public IReadOnlyCollection<Guid> LookupOverlays(string variableName) => overlays;

    public IReadOnlyList<string>? LookupLabelTexts(Guid overlayIdentifier) =>
        labelsByOverlay.TryGetValue(overlayIdentifier, out var texts) ? texts : null;

    public void UpsertOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts) =>
        throw new NotSupportedException();

    public void RemoveOverlay(Guid overlayIdentifier) => throw new NotSupportedException();

    public bool TrySeedOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts) =>
        throw new NotSupportedException();

    public IReadOnlyCollection<Guid> AllOverlays() => throw new NotSupportedException();
}
