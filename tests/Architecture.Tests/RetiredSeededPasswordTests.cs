namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// AS-6 (spec 325, issue #2510) — the sweep is complete. Plan.md §4's blast
/// radius lists 54+ <c>tests/Integration.Tests</c> files, four e2e sites,
/// the realm file itself, <c>README.md</c> and nine
/// <c>specs/*/quickstart.md</c> files carrying one of the six retired
/// seeded passwords. Phase 4b repoints every one of them at
/// <see cref="SmartSentinelEye.Integration.Tests.Fixtures.AspireFixture"/>'s
/// (now per-account) <c>SeededCredentials</c>; this guard is what proves the
/// sweep actually reached all of them rather than most.
///
/// <para>
/// <b>Colour (tasks.md phase 4a):</b> red today — phase 4b's data pass has
/// not landed, so every one of the six retired values is still exactly
/// where spec 219 found it.
/// </para>
///
/// <para>
/// The six retired values are never typed as literals here, mirroring
/// <see cref="SeededCredentialStrengthTests"/> — both compute them from the
/// same <c>Capitalise(local-part) + "1234"</c> rule, so this file cannot
/// itself be the thing that reintroduces a retired value into a text scan
/// of the repository.
/// </para>
///
/// <para>
/// Scope is exactly spec.md's AS-6: <c>README.md</c>,
/// <c>specs/*/quickstart.md</c>, <c>tests/</c>, <c>e2e/</c>, <c>src/</c>.
/// Historical spec artefacts
/// (<c>specs/*/{spec,plan,tasks,verification}.md</c>, ≈60 files that quote
/// what was true, not a runbook) and the three <c>scripts/</c> comments
/// quoting a since-deleted e2e spec line (spec.md §Out of scope) are out of
/// scope by construction — this scan never reads either location, rather
/// than reading and then skipping them.
/// </para>
/// </summary>
public class RetiredSeededPasswordTests
{
    private static readonly string[] ExcludedSegments = ["bin", "obj", "node_modules"];

    /// <summary>
    /// The six usernames whose retired password was
    /// <c>Capitalise(username) + suffix</c> — the four wall accounts took
    /// the <c>"-1234"</c> suffix, the two role accounts the bare
    /// <c>"1234"</c> one. The same pairing
    /// <c>SeededCredentialStrengthTests.RetiredSeededCredentials</c> names,
    /// kept as a private copy here rather than shared: the two files read
    /// different things (one a realm import, this one a repository-wide
    /// text scan) and ADR-0036 does not license widening a six-line table
    /// for a second call site that happens to agree today.
    /// </summary>
    private static readonly (string Username, string Suffix)[] RetiredSeededCredentials =
    [
        ("wall-munich", "-1234"),
        ("wall-dresden", "-1234"),
        ("wall-berlin", "-1234"),
        ("wall-hamburg", "-1234"),
        ("admin", "1234"),
        ("operator", "1234"),
    ];

    [Fact]
    public void No_retired_seeded_password_remains_in_readme_quickstarts_tests_e2e_or_src()
    {
        DirectoryInfo root = RepositorySource.Root();
        string[] retired = [.. RetiredSeededCredentials.Select(credential => Capitalise(credential.Username) + credential.Suffix)];
        string[] files = ScannedFiles(root);

        List<string> offenders = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string password in retired)
            {
                if (text.Contains(password, StringComparison.Ordinal))
                {
                    offenders.Add($"{RepositorySource.RelativePath(root, file)}: '{password}'");
                }
            }
        }

        offenders.ShouldBeEmpty(
            customMessage: $"expected none of the 6 retired seeded passwords to remain in "
            + "README.md, specs/*/quickstart.md, tests/, e2e/ or src/; found "
            + $"{offenders.Count} occurrence(s): {string.Join("; ", offenders)}");
    }

    /// <summary>
    /// Exactly spec.md AS-6's five roots. <c>README.md</c> is a single file;
    /// <c>specs/*/quickstart.md</c> is a one-level glob (so a historical
    /// <c>specs/NNN-x/spec.md</c> is never read); the other three are
    /// scanned recursively, excluding <see cref="ExcludedSegments"/>.
    /// </summary>
    private static string[] ScannedFiles(DirectoryInfo root)
    {
        List<string> files = [];

        string readme = Path.Combine(root.FullName, "README.md");
        if (File.Exists(readme))
        {
            files.Add(readme);
        }

        string specsDirectory = Path.Combine(root.FullName, "specs");
        if (Directory.Exists(specsDirectory))
        {
            files.AddRange(Directory.EnumerateFiles(specsDirectory, "quickstart.md", SearchOption.AllDirectories));
        }

        foreach (string tree in new[] { "tests", "e2e", "src" })
        {
            string directory = Path.Combine(root.FullName, tree);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            files.AddRange(
                Directory
                    .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(file => !file
                        .Split(Path.DirectorySeparatorChar)
                        .Any(segment => ExcludedSegments.Contains(segment, StringComparer.Ordinal))));
        }

        return [.. files.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
