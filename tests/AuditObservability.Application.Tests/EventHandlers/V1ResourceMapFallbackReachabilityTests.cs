using System.Reflection;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Pins the precondition that every convention-mapped contract either
/// declares a public <see cref="Guid"/> property or is excluded via a
/// hand-tweak: with no fallback allow-list in <see cref="V1ResourceMap"/>,
/// a convention-mapped contract with no <see cref="Guid"/> property has no
/// other way to resolve a resource identifier.
///
/// <para>
/// Derives the convention-mapped set from
/// <see cref="V1ResourceMap.Default"/>'s own <see cref="V1ResourceMap.MappedTypes"/>,
/// excluding whatever <see cref="V1ResourceMap.Conventions.HandTweaks"/> resolved,
/// via the <c>InternalsVisibleTo</c> grant added to
/// <c>SmartSentinelEye.AuditObservability.Application.csproj</c>. Reading
/// production's actual result — rather than re-deriving the namespace-tail
/// rule independently — means this guard cannot drift out of sync with
/// <c>ResolveResourceKind</c>: a future Guid-less convention-mapped contract
/// fails this test by name instead of silently falling through to a null
/// resource identifier.
/// </para>
/// </summary>
public class V1ResourceMapFallbackReachabilityTests
{
    [Fact]
    public void Every_convention_mapped_contract_declares_a_guid_property()
    {
        List<Type> conventionMapped = [.. V1ResourceMap.Default.MappedTypes
            .Where(type => !V1ResourceMap.Conventions.HandTweaks.ContainsKey(type))];

        conventionMapped.ShouldNotBeEmpty(
            "No convention-mapped contracts were found at all — this guard would pass " +
            "vacuously. Check that the reflection scan and NamespaceToResource conventions " +
            "still find real contracts.");

        List<Type> withoutGuid = [.. conventionMapped.Where(type => !DeclaresAGuidProperty(type))];

        withoutGuid.ShouldBeEmpty(
            "Convention-mapped contract(s) with no Guid property, which would resolve to a " +
            $"null resource identifier: {string.Join(", ", withoutGuid.Select(type => type.FullName))}. " +
            "Add a hand-tweak in V1ResourceMap.Conventions, or give the contract a Guid identifier " +
            "property.");
    }

    private static bool DeclaresAGuidProperty(Type type) =>
        Array.Exists(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.PropertyType == typeof(Guid));
}
