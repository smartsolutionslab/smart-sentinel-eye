using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards that every context's <c>FabIdentifier</c> can be ordered, ordinally
/// (spec 039, issue 1849).
///
/// <para>
/// Eight bounded contexts each define their own copy — value objects are not
/// shared across contexts (ADR-0044) and the grammar is deliberately identical
/// in all eight. None of them could be <em>ordered</em>, while
/// <c>CameraName</c> in the same folder could. Nobody decided that; it fell out
/// of nobody needing it until a sort reached for it.
/// </para>
///
/// <para>
/// What that costs is half an hour, repeatedly. <c>ListCamerasQueryHandler</c>
/// breaks ties on the fab, and <c>OrderBy</c>/<c>ThenBy</c> resolve
/// <c>Comparer&lt;T&gt;.Default</c> at <em>run</em> time — so the code compiles
/// and the deployed listing is correct (EF translates the whole chain to
/// <c>ORDER BY</c>), but a unit test whose rows tie throws
/// <c>At least one object must implement IComparable</c> from inside LINQ.
/// That message names neither the field being sorted nor the query doing the
/// sorting.
/// </para>
///
/// <para>
/// <b>Reads source rather than reflecting</b>, for two reasons. This test exists
/// for the <em>ninth</em> context, and a ninth Domain project added without a
/// reference to this test project would be invisible to reflection. And
/// ordinality is an argument inside a method body — there is no assembly-level
/// artefact for a <c>StringComparison</c>, so reflection could not assert it at
/// all. <c>StaleCodeConventionTests</c> and <c>NameMutabilityConventionTests</c>
/// read source for comparable reasons.
/// </para>
/// </summary>
public class FabOrderingConventionTests
{
    /// <summary>
    /// The record declaration itself, not the bare word. A doc comment
    /// mentioning <c>IComparable</c> must not satisfy this guard.
    /// </summary>
    private static readonly Regex ComparableDeclaration = new(
        @"record\s+FabIdentifier\s*:[^{]*\bIComparable<\s*FabIdentifier\s*>",
        RegexOptions.Compiled);

    [Fact]
    public void Every_FabIdentifier_can_be_ordered()
    {
        Dictionary<string, string> copies = ReadFabIdentifiers();

        string[] withoutOrdering = [.. copies
            .Where(copy => !ComparableDeclaration.IsMatch(copy.Value))
            .Select(copy => copy.Key)
            .Order(StringComparer.Ordinal)];

        withoutOrdering.ShouldBeEmpty(
            "these FabIdentifier copies cannot be ordered, so any OrderBy/ThenBy over one throws "
            + "'At least one object must implement IComparable' at run time — from inside LINQ, naming "
            + "neither the sort field nor the query. EF hides it in production by translating the sort "
            + "to SQL, so it surfaces only in tests, and only once two rows tie on the primary key. "
            + "Declare `: StringValueObject, IComparable<FabIdentifier>` and mirror CameraName "
            + "(specs/039-comparable-fab-identifier).");
    }

    /// <summary>
    /// The ordinality requirement, asserted <b>structurally</b>.
    ///
    /// <para>
    /// Spec 039 originally asked for a behavioural assertion — order a pair whose
    /// ordinal and culture-sensitive comparisons disagree. <b>No such pair exists
    /// under this grammar on this platform.</b> It admits lowercase ASCII
    /// letters, digits and <c>-</c> only, and seven probed pairs (including the
    /// classic hyphen-ignorability cases) agreed in sign under both comparisons,
    /// with globalization-invariant mode off. The character set is too small for
    /// them to differ.
    /// </para>
    ///
    /// <para>
    /// So this asserts the source instead, which is <em>stronger</em>: it holds
    /// for every input rather than for the one that happened to distinguish the
    /// two. <b>Do not "improve" it back into a behavioural test</b> — it cannot
    /// be written.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_FabIdentifier_orders_ordinally()
    {
        Dictionary<string, string> copies = ReadFabIdentifiers();

        string[] withoutOrdinal = [.. copies
            .Where(copy => !copy.Value.Contains("StringComparison.Ordinal", StringComparison.Ordinal))
            .Select(copy => copy.Key)
            .Order(StringComparer.Ordinal)];

        withoutOrdinal.ShouldBeEmpty(
            "these FabIdentifier copies do not compare ordinally. A culture-sensitive comparison can "
            + "order two fabs one way on a developer's machine and another on a CI runner, because ICU "
            + "behaviour varies by operating system and library version — and the caller that consults "
            + "this is a database tie-break whose whole purpose is a stable page boundary "
            + "(specs/039-comparable-fab-identifier).");
    }

    /// <summary>
    /// A source scan that silently matches nothing passes forever. That is the
    /// standard failure mode of this kind of test, and the one it cannot detect
    /// about itself, so it is asserted separately.
    /// </summary>
    [Fact]
    public void The_scan_finds_every_context_that_defines_a_fab_identifier()
    {
        Dictionary<string, string> copies = ReadFabIdentifiers();

        copies.Count.ShouldBeGreaterThanOrEqualTo(8,
            $"found {copies.Count} FabIdentifier files, and eight bounded contexts define one. "
            + "A scan matching nothing — a moved directory, a renamed file — passes every other "
            + "assertion here vacuously.");
    }

    /// <summary>
    /// Every <c>FabIdentifier.cs</c> under <c>src</c>, keyed by repo-relative
    /// path so a failure names the file. Mirrors
    /// <c>StaleCodeConventionTests.ReadSources</c>.
    /// </summary>
    private static Dictionary<string, string> ReadFabIdentifiers()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateFiles(src, "FabIdentifier.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(root.FullName, f), File.ReadAllText, StringComparer.Ordinal);
    }
}
