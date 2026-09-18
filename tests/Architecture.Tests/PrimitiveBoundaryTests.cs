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
/// twelve roots the walk reaches well over 100 types and has exactly the
/// surface the rule is about.
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
    /// Roots the walk starts from. Eleven aggregates reach it through
    /// <c>AggregateRoot&lt;T&gt;</c>; <c>AuditEvent</c> is append-only and carries
    /// state without that base, so it is named. A new aggregate needs no edit
    /// here unless it likewise skips the base.
    /// </summary>
    private static readonly string[] RootsWithoutAggregateRootBase = ["AuditEvent"];

    [Fact]
    public void No_domain_model_exposes_primitive_typed_state()
    {
        IReadOnlyList<string> offenders = Offenders(DomainAssemblies());

        offenders.ShouldBeEmpty(
            $"""
             Constitution §II: a domain model does not carry primitive-typed state.

             {string.Join(Environment.NewLine, offenders)}

             Introduce a value object, or — if the declaring type IS a value object
             and these are its own backing values — mark it with IValueObject
             (ADR-0066), which is what makes the exemption legible to this rule.
             """);
    }

    /// <summary>
    /// Confirmed by counterfactual, spec 184 (issue #2291): the walk records a
    /// banned type only when it is a property's <i>declared</i> type, not a
    /// generic argument or array element arriving as a constituent of it. A
    /// probe aggregate carrying <c>IReadOnlyList&lt;string&gt; Tags</c> is
    /// invisible to the walk below even though it violates the same rule as a
    /// bare <c>string</c> property would.
    ///
    /// <para>
    /// This fact is expected to fail today, on the unfixed walk, with the
    /// offender list holding only <c>ProbeAggregate.RawCount : Int32</c> — the
    /// positive control — while <c>Tags</c>, <c>Labels</c>, <c>Counters</c>,
    /// <c>Windows</c>, <c>Optionals</c> and <c>ProbeHighlight.Overlays</c> are
    /// absent. That asymmetry is the point: it proves the probe assembly is
    /// genuinely being walked, and that the gap is the collection/array
    /// constituent, not the harness.
    /// </para>
    /// </summary>
    [Fact]
    public void The_walk_sees_a_primitive_reached_through_a_collection_or_array()
    {
        IReadOnlyList<string> offenders = Offenders([typeof(PrimitiveBoundaryTests).Assembly]);

        offenders.ShouldBe(
        [
            "ProbeAggregate.Counters : Int32",
            "ProbeAggregate.Labels : String",
            "ProbeAggregate.Optionals : Int32",
            "ProbeAggregate.RawCount : Int32",
            "ProbeAggregate.Tags : String",
            "ProbeAggregate.Windows : DateTimeOffset",
            "ProbeHighlight.Overlays : Guid",
        ]);
    }

    /// <summary>
    /// The negative control for the fact above: constitution §II exempts "a
    /// value object's own backing values — plural", and a composite value
    /// object whose only backing value is a collection of primitives
    /// (<c>ProbeTagSet.Values</c>) is inside that exemption. A fix that widens
    /// the walk into collections must not start flagging it.
    /// </summary>
    [Fact]
    public void A_value_objects_own_backing_collection_is_exempt()
    {
        List<StateMember> members = WalkAggregateState([typeof(PrimitiveBoundaryTests).Assembly]);

        IReadOnlyList<string> exempted = [.. members
            .Where(member => member.DeclaringTypeIsValueObject)
            .Select(member => $"{member.DeclaringType.Name}.{member.Name}")
            .Distinct()];

        exempted.ShouldContain("ProbeTagSet.Values");
        exempted.ShouldContain("ProbeName.Value");

        IReadOnlyList<string> offenders = Offenders([typeof(PrimitiveBoundaryTests).Assembly]);

        offenders.ShouldNotContain("ProbeTagSet.Values : String");
        offenders.ShouldNotContain("ProbeName.Value : String");
    }

    [Fact]
    public void The_walk_reaches_every_aggregate_and_a_useful_amount_of_state()
    {
        (IReadOnlyList<Type> roots, int reached) = WalkFootprint();

        // A guard on the guard: if a refactor stops the walk reaching aggregate
        // state, the rule above silently passes everything. That failure is
        // invisible without this.
        roots.Count.ShouldBe(12);
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
        exempted.ShouldContain("NormalizedPosition.X");
    }

    /// <summary>
    /// The offender-filtering chain, shared between the real fact and the
    /// probe fact so a future change to the exemption logic cannot apply to
    /// one corpus and not the other.
    /// </summary>
    private static IReadOnlyList<string> Offenders(IReadOnlyList<Assembly> assemblies) =>
        [.. WalkAggregateState(assemblies)
            .Where(member => !member.Computed)
            .Where(member => !member.DeclaringTypeIsValueObject || IsIdentityReferenceInsideValueObject(member))
            .Select(member => $"{member.DeclaringType.Name}.{member.Name} : {member.PropertyType.Name}")
            .Distinct()
            .Order()];

    private static (IReadOnlyList<Type> Roots, int Reached) WalkFootprint()
    {
        IReadOnlyList<Type> roots = Roots();
        return (roots, WalkAggregateState().Select(member => member.DeclaringType).Distinct().Count());
    }

    private static IReadOnlyList<Type> Roots() => Roots(DomainAssemblies());

    private static IReadOnlyList<Type> Roots(IReadOnlyList<Assembly> assemblies) =>
        [.. assemblies
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

    private static List<StateMember> WalkAggregateState() => WalkAggregateState(DomainAssemblies());

    private static List<StateMember> WalkAggregateState(IReadOnlyList<Assembly> assemblies)
    {
        List<StateMember> members = [];
        HashSet<Type> seen = [];
        Queue<Type> pending = new(Roots(assemblies));
        IReadOnlyList<Type> allDomainTypes = [.. assemblies.SelectMany(assembly => assembly.GetExportedTypes())];

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

                foreach (Type constituent in Constituents(property.PropertyType))
                {
                    if (Banned.Contains(constituent))
                    {
                        members.Add(new StateMember(type, property.Name, constituent, isValueObject, IsComputed(type, property)));
                    }
                    else
                    {
                        pending.Enqueue(constituent);
                    }
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

    /// <summary>
    /// The set of types a property's declared type is built out of — itself,
    /// its generic arguments (recursively) and its array element type
    /// (recursively), each with <c>Nullable&lt;&gt;</c> stripped at every level.
    /// A banned constituent is recorded wherever it sits in that shape, not
    /// only when it is the declared type itself (issue #2291).
    ///
    /// <para>
    /// A fresh <c>visited</c> set per top-level call guards a self-referential
    /// generic closure from looping forever; it also happens to de-duplicate a
    /// repeated constituent (<c>IReadOnlyDictionary&lt;string, string&gt;</c>)
    /// within the one property. It is not shared across properties or across
    /// the outer walk's own <c>seen</c> set, which tracks dequeued types, not
    /// constituents computed before anything is enqueued.
    /// </para>
    ///
    /// <para>
    /// The array branch does not yield the array type itself, only what its
    /// element recurses into: an array's <see cref="Type.Namespace"/> is its
    /// <i>element's</i> namespace, so an array of a domain type would pass the
    /// <c>SmartSentinelEye</c> prefix filter at the outer walk and get walked
    /// into <see cref="Array"/>'s own public members (<c>Length</c>,
    /// <c>Rank</c>, …), producing spurious offenders. The element is already
    /// reached recursively, so the array type itself contributes nothing
    /// useful.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Type> Constituents(Type type) => [.. ConstituentsCore(type, [])];

    private static IEnumerable<Type> ConstituentsCore(Type type, HashSet<Type> visited)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(underlying))
        {
            yield break;
        }

        if (underlying.IsArray)
        {
            Type? elementType = underlying.GetElementType();
            if (elementType is not null)
            {
                foreach (Type constituent in ConstituentsCore(elementType, visited))
                {
                    yield return constituent;
                }
            }

            yield break;
        }

        // A generic type parameter (`T` on an open generic) is neither an
        // array nor a constructed generic type — it has no arguments to
        // recurse into and a null Namespace, which is harmless here and only
        // matters once the outer walk dequeues it.
        if (underlying.IsGenericType)
        {
            foreach (Type argument in underlying.GetGenericArguments())
            {
                foreach (Type constituent in ConstituentsCore(argument, visited))
                {
                    yield return constituent;
                }
            }

            yield return underlying;
            yield break;
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
