using System.Collections.Frozen;
using System.Reflection;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Kernel;
using DomainResourceKind = SmartSentinelEye.AuditObservability.Domain.AuditEvent.ResourceKind;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// Resource-pivot registry for every <c>*V1</c> in
/// <c>Shared.Contracts</c> (spec 009 FR-005 / FR-009).
///
/// <para>
/// Convention-first: each V1's namespace tail (e.g.
/// <c>Shared.Contracts.Automation.RuleCreatedV1</c> →
/// <c>"automation"</c>) is mapped to a <see cref="ResourceKind"/>
/// via a small dictionary; the resource identifier is picked from
/// the first property whose name appears in
/// <see cref="IdentifierPropertyNames"/>.
/// </para>
///
/// <para>
/// Hand-tweaks (e.g. for a V1 whose identifier is a business name
/// instead of a Guid, or a namespace whose tail doesn't match the
/// canonical resource vocabulary) sit in
/// <see cref="Conventions.HandTweaks"/>. Unmatched V1s still
/// audit, just with null resource fields (FR-005).
/// </para>
/// </summary>
public sealed partial class V1ResourceMap
{
    private static readonly string[] IdentifierPropertyNames =
    [
        "Identifier",
        "Name",
        "OverlayIdentifier",
        "CameraIdentifier",
        "LayoutIdentifier",
        "RuleIdentifier",
        "VariableIdentifier",
        "RegisteredClientIdentifier",
        "ChunkIdentifier",
        "WebhookIntegrationIdentifier",
        "DeadLetterIdentifier",
        "EventIdentifier",
    ];

    private readonly FrozenDictionary<Type, V1MappingEntry> _entries;
    private readonly HashSet<string> _explicitlyOptedOut;

    private V1ResourceMap(
        FrozenDictionary<Type, V1MappingEntry> entries,
        HashSet<string> explicitlyOptedOut)
    {
        _entries = entries;
        _explicitlyOptedOut = explicitlyOptedOut;
    }

    /// <summary>
    /// Default registry built from <c>Shared.Contracts</c>. Cached
    /// in a static so the reflection scan happens once per process.
    /// </summary>
    public static V1ResourceMap Default { get; } = BuildDefault();

    /// <summary>
    /// Returns the mapping for the given V1 type, or
    /// <see cref="V1Mapping.Unmapped"/> if the registry has no
    /// entry (so the audit row still gets written, just without
    /// the resource pivot).
    /// </summary>
    public V1Mapping Lookup(Type integrationEventType, object payloadInstance)
    {
        ArgumentNullException.ThrowIfNull(integrationEventType);
        if (!_entries.TryGetValue(integrationEventType, out V1MappingEntry? entry))
        {
            return V1Mapping.Unmapped;
        }

        ResourceIdentifier? identifier = entry.PickIdentifier(payloadInstance);
        return new V1Mapping(
            Kind: Option<DomainResourceKind>.Some(entry.Kind),
            ResourceIdentifier: identifier is null
                ? Option<ResourceIdentifier>.None
                : Option<ResourceIdentifier>.Some(identifier));
    }

    /// <summary>
    /// All <c>IIntegrationEvent</c> concrete types this registry
    /// can map. Used by the architecture test
    /// <c>V1ResourceMap_covers_every_IIntegrationEvent</c>
    /// (spec 009 T070).
    /// </summary>
    public IReadOnlyCollection<Type> MappedTypes => _entries.Keys;

    /// <summary>
    /// V1 type names explicitly marked as not-to-be-mapped via
    /// <see cref="Conventions.OptOut(string)"/>. The architecture
    /// test treats these as known gaps rather than failures.
    /// </summary>
    public IReadOnlySet<string> ExplicitlyOptedOut => _explicitlyOptedOut;

    private static V1ResourceMap BuildDefault()
    {
        Assembly contractsAssembly = typeof(IIntegrationEvent).Assembly;
        Dictionary<Type, V1MappingEntry> entries = new();

        foreach (Type type in contractsAssembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IIntegrationEvent).IsAssignableFrom(type)) continue;

            if (Conventions.HandTweaks.TryGetValue(type, out V1MappingEntry? tweak))
            {
                entries[type] = tweak;
                continue;
            }

            DomainResourceKind? kind = ResolveResourceKind(type);
            if (kind is null) continue;

            entries[type] = new V1MappingEntry(kind, BuildConventionPicker(type));
        }

        return new V1ResourceMap(entries.ToFrozenDictionary(), [.. Conventions.OptOuts]);
    }

    private static DomainResourceKind? ResolveResourceKind(Type type)
    {
        // Convention: the namespace tail maps to a canonical
        // resource — e.g. "Shared.Contracts.Automation" → "rule".
        string? leaf = type.Namespace?.Split('.').LastOrDefault();
        if (leaf is null) return null;
        return Conventions.NamespaceToResource.TryGetValue(leaf, out DomainResourceKind? mapped)
            ? mapped
            : null;
    }

    private static Func<object, ResourceIdentifier?> BuildConventionPicker(Type type)
    {
        PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Prefer the first Guid-typed property — every aggregate root in
        // the platform identifies itself with a Guid v7 (ADR-0039 / 0090),
        // so it's the most reliable signal across the V1 corpus.
        PropertyInfo? pick = Array.Find(props, property => property.PropertyType == typeof(Guid));

        // Fall back to the small allow-list of canonical property names
        // for V1s whose first Guid property is *not* the aggregate id
        // (e.g. it's an actor or a parent reference).
        if (pick is null)
        {
            foreach (string candidate in IdentifierPropertyNames)
            {
                pick = Array.Find(props, property => property.Name == candidate);
                if (pick is not null) break;
            }
        }

        if (pick is null) return _ => null;

        return instance =>
        {
            object? raw = pick.GetValue(instance);
            return raw is null ? null : ResourceIdentifier.From(raw.ToString()!);
        };
    }

    internal sealed record V1MappingEntry(
        DomainResourceKind Kind,
        Func<object, ResourceIdentifier?> PickIdentifier);
}
