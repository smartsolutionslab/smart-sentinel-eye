using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The repository as a source-scanning guard sees it. Spec 190 (issue #2257)
/// extracts this out of six guards that each carried their own copy —
/// <c>RepositoryRoot()</c> was byte-identical in all six (MD5-confirmed), and
/// <c>RelativePath</c>/<c>Relative</c> shared the same body and, in five of the
/// six, the same doc comment about <see cref="Path.GetRelativePath"/>'s platform
/// separator. <c>ExecutableLines</c> and its <c>StringLiteral</c> regex were
/// byte-identical in exactly two of the six —
/// <c>EndpointScopeDeclarationTests</c> and <c>PaginatedConsumerTests</c> — and
/// only those two are repointed here; the other four guards' <c>GuardSource</c>
/// self-scans are shaped differently and keep their own.
///
/// <para>
/// This is one of 31 files under <c>tests/</c> that carried the same
/// <c>RepositoryRoot()</c> walk on 2026-09-20 (28 in <c>Architecture.Tests</c>,
/// 3 in <c>Integration.Tests</c>). Spec 190 migrates the six the issue names;
/// the other 25 are recorded as a follow-up
/// (specs/190-one-reader-the-next-guard-finds/spec.md §1.2, §7) rather than
/// swept here.
/// </para>
/// </summary>
internal static class RepositorySource
{
    /// <summary>
    /// The content of a string literal, which is data rather than mechanism. The
    /// <c>GuardSource</c> self-scans read code, not the prose the code prints.
    /// </summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\\r\n]|\\.)*""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The directory holding <c>SmartSentinelEye.slnx</c>, walking up from
    /// <see cref="AppContext.BaseDirectory"/>. Byte-identical to the six copies
    /// it replaces.
    /// </summary>
    internal static DirectoryInfo Root()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// Forward slashes throughout. <see cref="Path.GetRelativePath(string, string)"/>
    /// returns the PLATFORM separator, so a filter or an expected string written
    /// with a backslash is green on a Windows developer machine and red on Linux
    /// CI — the worst direction for a guard to break, because it passes exactly
    /// where nobody looks. This repository has been bitten by exactly that.
    /// </summary>
    internal static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Lines that are neither commentary nor attribute metadata, with the
    /// content of every string literal removed — what is left is code that could
    /// carry a mechanism, rather than the prose it prints. Used by the
    /// <c>GuardSource</c> self-scans. Byte-identical in
    /// <c>EndpointScopeDeclarationTests</c> and <c>PaginatedConsumerTests</c>
    /// today; the other four guards' self-scans are shaped differently and keep
    /// their own.
    /// </summary>
    internal static IEnumerable<string> ExecutableLines(string source) =>
        source.Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith('['))
            .Select(line => StringLiteral.Replace(line, "\"\""));
}
