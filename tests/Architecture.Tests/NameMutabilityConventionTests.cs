using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the name-mutability convention (ADR-0120).
///
/// <para>
/// A name may be changed only where the aggregate is <b>not addressed by it</b>.
/// Where the name is the address, changing it is an identity change rather than
/// an attribute edit: every existing reference to the old name stops resolving,
/// and nothing in the system is obliged to notice.
/// </para>
///
/// <para>
/// The sharpest case is <c>Variable</c>. Its name is the address <em>and</em>
/// stored data in another bounded context —
/// <c>RuleAction.SetVariableValue(string VariableName, …)</c>, persisted with
/// the rule and read at evaluation — across a boundary ADR-0016 forbids a
/// project reference across. A rename there would leave rules that silently
/// stop firing, with no error raised anywhere.
/// </para>
///
/// <para>
/// Reads source rather than reflecting over assemblies, because the signal is a
/// route template string handed to <c>MapGet</c>/<c>MapPost</c> and there is no
/// assembly-level artefact to inspect. <c>StaleCodeConventionTests</c> reads
/// source for a comparable reason.
/// </para>
/// </summary>
public class NameMutabilityConventionTests
{
    /// <summary>
    /// A route parameter and its constraint, if any: <c>{camera:guid}</c>,
    /// <c>{revisionNumber:int}</c>, <c>{name}</c>.
    /// </summary>
    private static readonly Regex RouteParameter = new(
        @"Map(?:Get|Post|Put|Patch|Delete)\(""[^""]*\{(?<parameter>[A-Za-z][A-Za-z0-9]*)(?<constraint>:[a-z]+)?\}",
        RegexOptions.Compiled);

    /// <summary>
    /// A rename, however it is spelled. Deliberately broad: the convention is
    /// about the capability, not about one naming style.
    /// </summary>
    private static readonly Regex RenameDeclaration = new(
        @"\b(?:Rename[A-Za-z]*Command|Rename[A-Za-z]*Handler|(?:public|internal)\s+[A-Za-z<>,\s\[\]]*\s+Rename\s*\()",
        RegexOptions.Compiled);

    /// <summary>
    /// Not an aggregate address — a generic lookup over resources of any kind,
    /// which never had a name to rename.
    /// </summary>
    private static readonly string[] NotAnAggregateAddress = ["resourceIdentifier", "resourceKind"];

    [Fact]
    public void A_context_addressed_by_a_name_does_not_offer_to_change_it()
    {
        Dictionary<string, string> sources = ReadSources();

        Dictionary<string, string> addressedByValue = new(StringComparer.Ordinal);
        HashSet<string> offersRename = new(StringComparer.Ordinal);

        foreach ((string path, string source) in sources)
        {
            string context = ContextOf(path);
            if (context.Length == 0)
            {
                continue;
            }

            foreach (Match match in RouteParameter.Matches(source))
            {
                string parameter = match.Groups["parameter"].Value;

                // A constraint means the value is a typed identifier, not a name.
                if (match.Groups["constraint"].Success
                    || NotAnAggregateAddress.Contains(parameter, StringComparer.Ordinal))
                {
                    continue;
                }

                addressedByValue.TryAdd(context, parameter);
            }

            if (RenameDeclaration.IsMatch(source))
            {
                offersRename.Add(context);
            }
        }

        List<string> offenders =
        [
            .. offersRename
                .Where(addressedByValue.ContainsKey)
                .OrderBy(context => context, StringComparer.Ordinal)
                .Select(context => $"{context}: addressed by {{{addressedByValue[context]}}} and offers a rename"),
        ];

        offenders.ShouldBeEmpty(
            "ADR-0120: a name may be changed only where the aggregate is not addressed by it. The context(s) "
            + "above bind an unconstrained route parameter — so the name IS the address — and also expose a "
            + "rename. Changing it is an identity change, not an attribute edit: every existing reference to "
            + "the old name stops resolving, and for a Variable those references are stored in another "
            + "context's database with no integrity to catch the break. Either address the aggregate by an "
            + "identifier (add a ':guid' constraint and key on it, as Camera and Layout do), or drop the "
            + "rename and treat the correction as create-then-archive.");
    }

    /// <summary>
    /// The counterpart. The check above passes just as happily against a
    /// codebase with no routes at all, or one where the regex silently stopped
    /// matching — so something must be known to be found.
    ///
    /// <para>
    /// Deliberately asserts a <em>lower bound</em> and named members rather than
    /// an exact set. ADR-0119's equivalent pinned an exact list of eight, which
    /// was right for a closed vocabulary; here the population is open and an
    /// exact list would fail every time a context gains a route.
    /// </para>
    /// </summary>
    [Fact]
    public void The_name_addressed_contexts_are_still_found()
    {
        Dictionary<string, string> sources = ReadSources();

        HashSet<string> addressedByValue = new(StringComparer.Ordinal);

        foreach ((string path, string source) in sources)
        {
            string context = ContextOf(path);
            if (context.Length == 0)
            {
                continue;
            }

            foreach (Match match in RouteParameter.Matches(source))
            {
                if (!match.Groups["constraint"].Success
                    && !NotAnAggregateAddress.Contains(match.Groups["parameter"].Value, StringComparer.Ordinal))
                {
                    addressedByValue.Add(context);
                }
            }
        }

        addressedByValue.ShouldContain(
            "Automation",
            "Automation addresses rules by {name} (ADR-0120). If this no longer holds, either the routes "
            + "changed — in which case the rulings in ADR-0120 need revisiting — or the detection above "
            + "stopped working and the convention is now unguarded.");

        addressedByValue.ShouldContain("SystemVariables", "SystemVariables addresses variables by {name}.");

        // Not in the ADR's original table of five. Found while designing this
        // check, which is the argument for the check in one line.
        addressedByValue.ShouldContain("EventIngestion", "EventIngestion addresses integrations by {integrationName}.");
        addressedByValue.ShouldContain("Identity", "Identity addresses devices and kiosks by {clientId}.");

        addressedByValue.ShouldNotContain(
            "CameraCatalog",
            "CameraCatalog addresses cameras by {camera:guid}, which is what makes a camera renameable at "
            + "all (ADR-0120). If it appears here, an endpoint has started binding an unconstrained "
            + "parameter and the rename shipped by spec 033 is no longer safe.");
    }

    /// <summary>The bounded context a source file belongs to, or empty.</summary>
    private static string ContextOf(string relativePath)
    {
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2 && string.Equals(segments[0], "src", StringComparison.Ordinal)
            ? segments[1]
            : string.Empty;
    }

    private static Dictionary<string, string> ReadSources()
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
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(root.FullName, f), File.ReadAllText, StringComparer.Ordinal);
    }
}
