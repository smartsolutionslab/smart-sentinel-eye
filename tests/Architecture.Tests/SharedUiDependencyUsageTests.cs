using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US5 of spec 266 (issue #2335): a Radix package installed in
/// <c>apps/shared/package.json</c> and imported by nothing is an audit
/// finding waiting to happen rather than a build failure. This makes it one.
///
/// <para>
/// Every <c>"@radix-ui/*"</c> key under <c>dependencies</c> must be imported
/// (<c>from '&lt;package&gt;'</c>, after comments are stripped) by at least
/// one non-test <c>.ts</c>/<c>.tsx</c> file under <c>apps/shared/src</c>.
/// Mirrors <see cref="SharedUiTokenUsageTests"/> for <c>RepositoryRoot()</c>
/// (via <see cref="RepositorySource"/>), comment stripping and
/// slash-normalised relative paths — <see cref="RepositorySource.RelativePath"/>
/// already does the normalising, so this guard inherits it rather than
/// re-deriving its own (a backslash literal here would be green on a Windows
/// developer machine and red on Linux CI, exactly the failure mode that
/// class's own remarks record).
/// </para>
///
/// <para>
/// <b>Red on develop:</b> <c>@radix-ui/react-dropdown-menu</c>,
/// <c>@radix-ui/react-popover</c>, <c>@radix-ui/react-select</c> and
/// <c>@radix-ui/react-tabs</c> are declared but imported by nothing — spec
/// 266's whole premise (issue #2335). It turns green only once each of
/// US1–US4 lands a real (non-stub) primitive that imports its package, or
/// Q3 is answered "defer" and <c>react-tabs</c> is removed from
/// <c>dependencies</c> instead.
/// </para>
/// </summary>
public class SharedUiDependencyUsageTests
{
    private const string PackageJsonRelativePath = "apps/shared/package.json";
    private const string SharedUiSourceRoot = "apps/shared/src";

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex LineComment = new(@"//[^\n]*");

    [Fact]
    public void Every_radix_package_the_shared_workspace_declares_is_imported()
    {
        DirectoryInfo root = RepositorySource.Root();

        IReadOnlyList<string> radixPackages = DeclaredRadixPackages(root);
        string sourceText = CommentStrippedSharedUiSource(root);

        List<string> unused = [.. radixPackages.Where(package => !ImportsPackage(sourceText, package))];

        unused.ShouldBeEmpty(
            $"{unused.Count} Radix package(s) declared in {PackageJsonRelativePath} are imported by nothing under "
            + $"{SharedUiSourceRoot} (excluding tests): {string.Join(", ", unused)} — import it from a primitive, "
            + "or remove it from dependencies.");
    }

    // The counterfactual (spec 266 plan.md §5.1) is deliberately NOT a fact
    // here: "in a scratch copy, add @radix-ui/react-switch to dependencies,
    // run the fact, quote the failure, revert" is a one-off demonstration for
    // the PR body, not a permanent suite member — a committed counterfactual
    // fact would itself need to stay in sync with whatever the real
    // dependency list becomes, for no benefit over re-running the one fact
    // above against a scratch-edited package.json by hand.

    private static IReadOnlyList<string> DeclaredRadixPackages(DirectoryInfo root)
    {
        string packageJsonPath = Path.Combine(root.FullName, PackageJsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

        return [.. document.RootElement.GetProperty("dependencies")
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(name => name.StartsWith("@radix-ui/", StringComparison.Ordinal))];
    }

    private static string CommentStrippedSharedUiSource(DirectoryInfo root)
    {
        return string.Join('\n', ScannedFiles(root).Select(relativePath => StripComments(
            File.ReadAllText(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar))))));
    }

    private static bool ImportsPackage(string commentStrippedSource, string package) =>
        commentStrippedSource.Contains($"from '{package}'", StringComparison.Ordinal)
        || commentStrippedSource.Contains($"from \"{package}\"", StringComparison.Ordinal);

    private static IEnumerable<string> ScannedFiles(DirectoryInfo root)
    {
        string full = Path.Combine(root.FullName, SharedUiSourceRoot.Replace('/', Path.DirectorySeparatorChar));

        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".tsx", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = RepositorySource.RelativePath(root, file);

            if (relative.Contains(".test.", StringComparison.Ordinal) || relative.Contains("/test/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static string StripComments(string source)
    {
        string withoutBlockComments = BlockComment.Replace(source, string.Empty);
        return LineComment.Replace(withoutBlockComments, string.Empty);
    }
}
