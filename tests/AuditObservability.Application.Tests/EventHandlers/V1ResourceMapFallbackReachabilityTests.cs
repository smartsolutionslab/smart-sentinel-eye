using System.Reflection;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Spec 226 FR-007 — pins the precondition that makes
/// <see cref="V1ResourceMap"/>'s <c>IdentifierPropertyNames</c> allow-list
/// fallback unreachable (spec 226 §0.2): every contract the convention
/// picker builds for declares at least one public <see cref="Guid"/>
/// property, so <c>BuildConventionPicker</c>'s <c>pick is null</c> branch is
/// never taken.
///
/// <para>
/// Uses the <b>preferred form</b> from plan §5.2: derives the
/// convention-mapped set from <see cref="V1ResourceMap.Conventions.HandTweaks"/>
/// and <see cref="V1ResourceMap.Conventions.NamespaceToResource"/> at test
/// time, via the <c>InternalsVisibleTo</c> grant added to
/// <c>SmartSentinelEye.AuditObservability.Application.csproj</c>. Nothing is
/// hard-coded, so the guard stays correct when a hand-tweak is added or
/// removed: a future Guid-less convention-mapped contract fails this test by
/// name instead of silently falling through to a null resource identifier.
/// </para>
/// </summary>
public class V1ResourceMapFallbackReachabilityTests
{
    [Fact]
    public void Every_convention_mapped_contract_declares_a_guid_property()
    {
        Assembly contracts = typeof(IIntegrationEvent).Assembly;

        List<Type> conventionMapped = [.. contracts.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type))
            .Where(type => !V1ResourceMap.Conventions.HandTweaks.ContainsKey(type))
            .Where(IsConventionMapped)];

        List<Type> withoutGuid = [.. conventionMapped.Where(type => !DeclaresAGuidProperty(type))];

        withoutGuid.ShouldBeEmpty(
            "Convention-mapped contract(s) with no Guid property, which would reach the " +
            $"IdentifierPropertyNames fallback: {string.Join(", ", withoutGuid.Select(type => type.FullName))}. " +
            "Add a hand-tweak in V1ResourceMap.Conventions, or confirm the fallback allow-list still names " +
            "the right property.");
    }

    private static bool IsConventionMapped(Type type)
    {
        string? leaf = type.Namespace?.Split('.').LastOrDefault();
        return leaf is not null && V1ResourceMap.Conventions.NamespaceToResource.ContainsKey(leaf);
    }

    private static bool DeclaresAGuidProperty(Type type) =>
        Array.Exists(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.PropertyType == typeof(Guid));
}
