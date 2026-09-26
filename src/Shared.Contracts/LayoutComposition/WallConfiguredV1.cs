namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event raised when a <c>Wall</c> is created or its scene set is
/// edited (spec 258 US1, ADR-0157, ADR-0158). Carries the full ordered scene
/// list and the scene currently showing, so a downstream subscriber (audit,
/// the kiosk picker) needs no follow-up read to know the wall's shape.
///
/// <para>
/// <paramref name="Scenes"/> holds <c>LayoutIdentifier</c> values (ADR-0158:
/// a scene references a Layout chain, not a pinned Revision), primitive at
/// the wire per ADR-0040.
/// </para>
/// </summary>
public sealed record WallConfiguredV1(
    Guid Wall,
    string Name,
    IReadOnlyList<Guid> Scenes,
    Guid Showing,
    DateTimeOffset ConfiguredAt,
    EventMetadata Metadata) : IIntegrationEvent
{
    /// <summary>
    /// The compiler-synthesised record equality compares <see cref="Scenes"/>
    /// by reference (a <see cref="List{T}"/> or array does not override
    /// <c>Equals</c>), so two events built from the same values — or a value
    /// deserialised from JSON — would compare unequal. Overridden here to
    /// compare the scene list by its elements, in order; order is part of the
    /// value (FR-002's ordered scene set), so this is not
    /// order-independent equality.
    /// </summary>
    public bool Equals(WallConfiguredV1? other) =>
        other is not null
        && Wall == other.Wall
        && Name == other.Name
        && Scenes.SequenceEqual(other.Scenes)
        && Showing == other.Showing
        && ConfiguredAt == other.ConfiguredAt
        && Metadata == other.Metadata;

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Wall);
        hash.Add(Name);
        foreach (Guid scene in Scenes)
        {
            hash.Add(scene);
        }
        hash.Add(Showing);
        hash.Add(ConfiguredAt);
        hash.Add(Metadata);
        return hash.ToHashCode();
    }
}
