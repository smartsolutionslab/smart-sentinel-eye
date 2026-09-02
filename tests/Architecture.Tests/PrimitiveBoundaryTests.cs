using System.Reflection;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Enforces constitution §II (ADR-0139 as amended by ADR-0140): a domain model
/// does not carry primitive-typed state.
///
/// <para>
/// <b>The exemptions are what make this hard, not the ban.</b> Every value
/// object is a keyword-typed member by construction — <c>CameraName</c> wraps a
/// <c>string</c>, <c>CameraIdentifier</c> wraps a <c>Guid</c> — so a rule that
/// simply flags banned types flags all ~79 of them first and gets switched off.
/// </para>
///
/// <para>
/// <b>So this rule walks from the aggregates outward</b> rather than scanning
/// every type in a Domain assembly. §II binds what a domain model exposes as
/// state; a notification record or a port's return shape is neither, and
/// scanning by assembly flags 30 of those before it finds anything real. From
/// eleven roots the walk reaches 133 types and has exactly the surface the rule
/// is about.
/// </para>
///
/// <para>
/// Two regressions this repository has actually made are the reason for the
/// shape. <c>Tile.Row</c>/<c>Col</c> were <c>int</c>s on an entity that
/// reconstructed a <c>GridPosition</c> from them, and passed a human survey by
/// being described as "already inside value objects" — a rule keyed on the
/// declaring type's *name* would pass it too, which is why this one asks
/// whether the declaring type implements <see cref="IValueObject"/>. And
/// <c>HighlightOverlay</c> held a raw <c>Guid</c> overlay reference inside a
/// composite value object, which is why ADR-0140 added that an identity
/// reference is never a backing value.
/// </para>
/// </summary>
public class PrimitiveBoundaryTests
{
    /// <summary>
    /// §II's banned set, as a category: every C# predefined type plus the named
    /// BCL types that carry no domain meaning. Spelled out because reflection
    /// cannot ask "does this type have a language keyword?".
    /// </summary>
    private static readonly HashSet<Type> Banned =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(decimal),
        typeof(double), typeof(float), typeof(int), typeof(uint), typeof(nint),
        typeof(nuint), typeof(long), typeof(ulong), typeof(short), typeof(ushort),
        typeof(string), typeof(object),
        typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly),
        typeof(TimeOnly), typeof(TimeSpan), typeof(Uri),
    ];

    /// <summary>
    /// Roots the walk starts from. Nine aggregates reach it through
    /// <c>AggregateRoot&lt;T&gt;</c>; <c>AuditEvent</c> is append-only and carries
    /// state without that base, so it is named. A new aggregate needs no edit
    /// here unless it likewise skips the base.
    /// </summary>
    private static readonly string[] RootsWithoutAggregateRootBase = ["AuditEvent"];

    [Fact]
    public void No_domain_model_exposes_primitive_typed_state()
    {
        IReadOnlyList<string> offenders = [.. WalkAggregateState()
            .Where(member => !member.Computed)
            .Where(member => !member.DeclaringTypeIsValueObject || IsIdentityReferenceInsideValueObject(member))
            .Select(member => $"{member.DeclaringType.Name}.{member.Name} : {member.PropertyType.Name}")
            .Distinct()
            .Order()];

        offenders.ShouldBeEmpty(
            $"""
             Constitution §II: a domain model does not carry primitive-typed state.

             {string.Join(Environment.NewLine, offenders)}

             Introduce a value object, or — if the declaring type IS a value object
             and these are its own backing values — mark it with IValueObject
             (ADR-0066), which is what makes the exemption legible to this rule.
             """);
    }

    [Fact]
    public void The_walk_reaches_every_aggregate_and_a_useful_amount_of_state()
    {
        (IReadOnlyList<Type> roots, int reached) = WalkFootprint();

        // A guard on the guard: if a refactor stops the walk reaching aggregate
        // state, the rule above silently passes everything. That failure is
        // invisible without this.
        roots.Count.ShouldBe(11);
        reached.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void A_value_objects_own_backing_values_are_exempt()
    {
        IReadOnlyList<string> exempted = [.. WalkAggregateState()
            .Where(member => member.DeclaringTypeIsValueObject)
            .Select(member => $"{member.DeclaringType.Name}.{member.Name}")
            .Distinct()];

        // The exemption is load-bearing, not incidental: if it ever stops
        // applying, the rule above starts failing on ~79 legitimate types.
        exempted.ShouldContain("CameraName.NormalizedValue");
        exempted.ShouldContain("GridPosition.Row");
        exempted.ShouldContain("Label.NormalizedX");
    }

    private static (IReadOnlyList<Type> Roots, int Reached) WalkFootprint()
    {
        IReadOnlyList<Type> roots = Roots();
        return (roots, WalkAggregateState().Select(member => member.DeclaringType).Distinct().Count());
    }

    private static IReadOnlyList<Type> Roots() =>
        [.. DomainAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => DerivesFromAggregateRoot(type)
                           || RootsWithoutAggregateRootBase.Contains(type.Name))];

    /// <summary>
    /// Loaded from disk rather than read off <c>AppDomain.CurrentDomain</c>.
    /// A referenced assembly is not loaded until something touches a type in it,
    /// so the AppDomain list is empty here and the whole walk would find nothing
    /// — the rule would pass by reaching no state at all. That is what
    /// <c>The_walk_reaches_every_aggregate_and_a_useful_amount_of_state</c> is
    /// for, and it caught exactly this on the rule's first run.
    /// </summary>
    private static IReadOnlyList<Assembly> DomainAssemblies() =>
        [.. Directory
            .GetFiles(AppContext.BaseDirectory, "SmartSentinelEye.*.Domain.dll")
            .Select(Assembly.LoadFrom)];

    private static bool DerivesFromAggregateRoot(Type type)
    {
        for (Type? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType
                && baseType.GetGenericTypeDefinition().Name.StartsWith("AggregateRoot", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A <c>Guid</c> on a value object is a legitimate backing value only where the
    /// type declares <c>IValueObject&lt;Guid&gt;</c> — a single-valued identifier
    /// wrapper. A <c>Guid</c> sitting inside a <i>composite</i> value object is an
    /// identity reference, and ADR-0140 says an identity reference is never a
    /// backing value. That is <c>HighlightOverlay(Guid Overlay, …)</c>, and it is
    /// the one part of ADR-0140 reflection can check: nothing in this codebase
    /// composes a value object out of a raw <c>Guid</c> alongside other members.
    /// </summary>
    private static bool IsIdentityReferenceInsideValueObject(StateMember member) =>
        member.PropertyType == typeof(Guid)
        && !typeof(IValueObject<Guid>).IsAssignableFrom(member.DeclaringType);

    private static List<StateMember> WalkAggregateState()
    {
        List<StateMember> members = [];
        HashSet<Type> seen = [];
        Queue<Type> pending = new(Roots());
        IReadOnlyList<Type> allDomainTypes = [.. DomainAssemblies().SelectMany(assembly => assembly.GetExportedTypes())];

        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            if (!seen.Add(type) || type.Namespace?.StartsWith("SmartSentinelEye", StringComparison.Ordinal) != true)
            {
                continue;
            }

            bool isValueObject = typeof(IValueObject).IsAssignableFrom(type);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == "PendingEvents")
                {
                    continue;
                }

                Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (Banned.Contains(propertyType))
                {
                    members.Add(new StateMember(type, property.Name, propertyType, isValueObject, IsComputed(type, property)));
                    continue;
                }

                foreach (Type reachable in Unwrap(property.PropertyType))
                {
                    pending.Enqueue(reachable);
                }
            }

            // A discriminated union's cases are reachable state even though the
            // aggregate only names the abstract parent.
            if (type.IsAbstract)
            {
                foreach (Type subtype in allDomainTypes.Where(candidate => type.IsAssignableFrom(candidate) && candidate != type))
                {
                    pending.Enqueue(subtype);
                }
            }
        }

        return members;
    }

    /// <summary>
    /// A value the model computes rather than stores — `IsRevoked`, `IsSystem`.
    /// §II binds state, not derived answers (ADR-0140).
    ///
    /// <para>
    /// <b>Restricted to <c>bool</c> deliberately.</b> Reflection cannot tell
    /// <c>IsRevoked =&gt; RevokedAt is not null</c> from <c>Row =&gt; row</c>: both are
    /// get-only with no backing field, and the second is storage wearing a
    /// computed disguise — the exact shape of the <c>Tile</c> defect. Every
    /// non-state answer this codebase exposes as a property is a predicate, so
    /// anything else get-only and primitive-typed is treated as state. Caught by
    /// planting <c>Tile.Row =&gt; row</c> and watching an earlier version of this
    /// rule pass it.
    /// </para>
    /// </summary>
    private static bool IsComputed(Type type, PropertyInfo property) =>
        property.PropertyType == typeof(bool)
        && type.GetField($"<{property.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance) is null
        && property.GetSetMethod(nonPublic: true) is null;

    private static IEnumerable<Type> Unwrap(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsGenericType)
        {
            foreach (Type argument in underlying.GetGenericArguments())
            {
                yield return argument;
            }
        }
        yield return underlying;
    }

    private sealed record StateMember(
        Type DeclaringType,
        string Name,
        Type PropertyType,
        bool DeclaringTypeIsValueObject,
        bool Computed);
}
