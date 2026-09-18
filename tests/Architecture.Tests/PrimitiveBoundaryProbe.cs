using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// These types deliberately violate constitution §II and exist only to be
/// caught by <see cref="PrimitiveBoundaryTests"/>'s walk — they must never be
/// "fixed" by wrapping their primitive-typed state in value objects, and no
/// change here should ever make the probe compliant.
///
/// <para>
/// The expected offender list asserted in
/// <c>PrimitiveBoundaryTests.The_walk_sees_a_primitive_reached_through_a_collection_or_array</c>
/// is exact, not a superset check. Adding, removing or retyping a member below
/// requires updating that list in the same change, or the probe and the
/// assertion will silently drift apart and stop testing what they claim to.
/// </para>
/// </summary>
public readonly record struct ProbeIdentifier(Guid Value) : IStronglyTypedId<Guid>;

/// <summary>
/// Single-valued value object. <c>Value</c> is the value object's own backing
/// value and must be exempt, never a reported offender.
/// </summary>
public sealed record ProbeName(string Value) : IValueObject<string>;

/// <summary>
/// Composite value object whose only backing value is a collection of
/// primitives. Must be exempt in full: constitution §II exempts "a value
/// object's own backing values — plural" — a collection of primitives is
/// still the value object's own backing value, not state exposed raw.
/// </summary>
public sealed record ProbeTagSet(IReadOnlyList<string> Values) : IValueObject;

/// <summary>
/// Composite value object carrying identity references inside a collection.
/// ADR-0140: an identity reference is never a backing value, so
/// <c>Overlays</c> must be flagged even though it belongs to a value object —
/// the same rule the walk already applies to a bare <c>Guid</c> member,
/// reached here through a collection instead.
/// </summary>
public sealed record ProbeHighlight(IReadOnlyList<Guid> Overlays, ProbeName Label) : IValueObject;

/// <summary>
/// The roots-bearing probe aggregate. Every member is deliberately shaped to
/// land on one side or the other of the walk's collection/array blind spot —
/// see the file-level doc comment before touching any of them.
/// </summary>
public sealed class ProbeAggregate : AggregateRoot<ProbeIdentifier>
{
    /// <summary>
    /// The positive control: a declared-primitive property the unfixed walk
    /// already catches. Its presence in the offender list proves the probe
    /// assembly is genuinely being walked.
    /// </summary>
    // Non-default initializer so the private setter is genuinely exercised — S1144 flags it otherwise.
    public int RawCount { get; private set; } = 1;

    /// <summary>The issue's exact shape: a primitive behind a single generic argument.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>The array spelling of the same hole — <c>Unwrap</c> does not handle arrays at all.</summary>
    public string[] Labels { get; private set; } = [];

    /// <summary>
    /// A dictionary's value argument is a primitive; the key is a value
    /// object and must stay unflagged either way.
    /// </summary>
    public IReadOnlyDictionary<ProbeName, int> Counters { get; private set; } = new Dictionary<ProbeName, int>();

    /// <summary>Two levels of nesting — a primitive behind two generic arguments.</summary>
    public IReadOnlyList<IReadOnlyList<DateTimeOffset>> Windows { get; private set; } = [];

    /// <summary>A nullable primitive inside a generic argument, needing <c>Nullable&lt;&gt;</c> stripped per argument.</summary>
    public IReadOnlyList<int?> Optionals { get; private set; } = [];

    /// <summary>A collection of a domain type, not a primitive — must stay unflagged.</summary>
    public IReadOnlyList<ProbeName> Names { get; private set; } = [];

    /// <summary>Reaches <see cref="ProbeTagSet.Values"/>, a value object's own exempt backing collection.</summary>
    public ProbeTagSet Tagging { get; private set; } = new([]);

    /// <summary>Reaches <see cref="ProbeHighlight.Overlays"/>, which must be flagged (ADR-0140).</summary>
    public ProbeHighlight Highlight { get; private set; } = new([], new ProbeName("probe"));
}
