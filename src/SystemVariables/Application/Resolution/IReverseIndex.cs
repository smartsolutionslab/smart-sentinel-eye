namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// In-memory reverse-index mapping variable name → overlays that
/// reference it in their label text (spec 005 plan.md). Also caches
/// each overlay's current label text for fast resolution.
///
/// <para>
/// The infrastructure impl is a singleton backed by a
/// <c>ConcurrentDictionary</c>; this interface keeps the Application
/// layer free of concurrency-primitive details for testability.
/// </para>
/// </summary>
public interface IReverseIndex
{
    /// <summary>
    /// Re-parses the overlay's label, replaces any previous references
    /// for that overlay, and caches the label text for resolution.
    /// </summary>
    void UpsertOverlayReferences(Guid overlayIdentifier, string labelText);

    /// <summary>
    /// Removes every reference from the given overlay and drops its
    /// cached label text. Called on <c>OverlayRevisionArchivedV1</c>.
    /// </summary>
    void RemoveOverlay(Guid overlayIdentifier);

    /// <summary>
    /// Returns the overlay identifiers whose labels reference the
    /// given variable name. Empty if the name isn't referenced.
    /// </summary>
    IReadOnlyCollection<Guid> LookupOverlays(string variableName);

    /// <summary>
    /// Returns the cached label text for an overlay, or <c>null</c> if
    /// the overlay is unknown to the index (i.e., never published, or
    /// archived). Used by the resolver to recompute resolved text.
    /// </summary>
    string? LookupLabelText(Guid overlayIdentifier);

    /// <summary>
    /// Returns every overlay identifier currently held in the index.
    /// Used by the variable-archived handler to walk and re-resolve.
    /// </summary>
    IReadOnlyCollection<Guid> AllOverlays();

    /// <summary>
    /// Increments and returns the per-overlay monotonic version
    /// counter. Used by the push fan-out so kiosks can drop
    /// out-of-order frames. Thread-safe.
    /// </summary>
    long NextVersionFor(Guid overlayIdentifier);

    /// <summary>
    /// Current version for an overlay without incrementing it. Used by
    /// the snapshot read path so clients can ignore older pushes after
    /// a fresh GET.
    /// </summary>
    long CurrentVersionFor(Guid overlayIdentifier);
}
