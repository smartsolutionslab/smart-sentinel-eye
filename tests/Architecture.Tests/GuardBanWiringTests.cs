namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the guard ban (ADR-0139).
///
/// <para>
/// The ban itself is a compile error — <c>RS0030</c> at <c>error</c> severity —
/// so no test is needed to catch a banned call. What no compile error can catch
/// is the ban going <b>unread</b>. If the additional-files entry is dropped, the
/// list is renamed, or the analyzer package stops flowing to a project, every
/// build goes green and stays green. Silence is the failure mode, and silence is
/// indistinguishable from compliance.
/// </para>
///
/// <para>
/// That is not hypothetical here. The list must be named exactly
/// <c>BannedSymbols.txt</c>: the analyzer matches additional files by the prefix
/// <c>BannedSymbols.</c>, and multi-file support has regressed once already
/// (roslyn-analyzers#5622). The obvious name for a second list —
/// <c>BannedSymbols.Guards.txt</c> — was this feature's first proposal and would
/// have tied the ban to undocumented behaviour. Two lists, both named
/// <c>BannedSymbols.txt</c>, distinguished by directory.
/// </para>
///
/// <para>
/// Reads repository files rather than reflecting over assemblies, because the
/// thing under test is build configuration, which leaves no trace in IL.
/// <c>StaleCodeConventionTests</c> and <c>FoundingDecisionRecordTests</c> read
/// source for comparable reasons.
/// </para>
/// </summary>
public class GuardBanWiringTests
{
    private const string GuardBanList = "build/guards/BannedSymbols.txt";

    /// <summary>
    /// Every BCL argument-precondition helper ADR-0139 bans. Three of these had
    /// zero call sites when the ban was written and are listed anyway: a
    /// prohibition exists for the call site nobody has written yet, and banning
    /// one of a pair while its sibling stays legal reads as a deliberate
    /// carve-out.
    /// </summary>
    private static readonly string[] BannedHelpers =
    [
        "System.ArgumentNullException.ThrowIfNull",
        "System.ArgumentException.ThrowIfNullOrWhiteSpace",
        "System.ArgumentException.ThrowIfNullOrEmpty",
        "System.ArgumentOutOfRangeException.ThrowIfLessThan",
        "System.ArgumentOutOfRangeException.ThrowIfGreaterThan",
        "System.ArgumentOutOfRangeException.ThrowIfNegative",
    ];

    [Fact]
    public void The_guard_ban_names_every_helper_it_replaces()
    {
        string list = ReadRepositoryFile(GuardBanList);

        string[] missing = BannedHelpers
            .Where(helper => !list.Contains($"M:{helper}", StringComparison.Ordinal))
            .ToArray();

        missing.ShouldBeEmpty(
            "ADR-0139 bans every BCL argument-precondition helper, not the subset currently in use. "
            + $"The helper(s) above are absent from {GuardBanList}, so a call to one compiles cleanly "
            + "while its already-banned sibling does not — which reads to the next engineer as a "
            + "deliberate carve-out rather than an omission.");
    }

    [Fact]
    public void The_guard_ban_is_actually_read_by_the_analyzer()
    {
        string properties = ReadRepositoryFile("Directory.Build.props");

        properties.ShouldContain(
            @"build\guards\BannedSymbols.txt",
            Case.Sensitive,
            $"{GuardBanList} exists but nothing includes it as an AdditionalFiles entry, so the analyzer "
            + "never reads it and every banned call compiles. The ban would be entirely inert while "
            + "looking, from a green build, exactly like full compliance.");
    }

    /// <summary>
    /// The filename is load-bearing, not cosmetic — see the class remarks.
    /// </summary>
    [Fact]
    public void Both_ban_lists_are_named_what_the_analyzer_matches()
    {
        DirectoryInfo root = RepositoryRoot();

        File.Exists(Path.Combine(root.FullName, "BannedSymbols.txt")).ShouldBeTrue(
            "the ConfigureAwait ban (ADR-0049) is expected at the repository root.");

        File.Exists(Path.Combine(root.FullName, "build", "guards", "BannedSymbols.txt")).ShouldBeTrue(
            "the guard ban (ADR-0139) is expected at build/guards/BannedSymbols.txt. Both lists carry "
            + "the same filename and differ only by directory, because the analyzer matches additional "
            + "files on the 'BannedSymbols.' prefix and multi-file support has regressed before. "
            + "Renaming either one to something more descriptive silently disables it.");
    }

    /// <summary>
    /// The counterpart the three checks above cannot supply: they would all pass
    /// against a ban that reaches nothing. The two exemptions are what bound the
    /// ban's scope, so they are asserted rather than assumed.
    /// </summary>
    [Fact]
    public void The_ban_reaches_everything_except_its_two_recorded_exemptions()
    {
        string properties = ReadRepositoryFile("Directory.Build.props");
        string editorconfig = ReadRepositoryFile(".editorconfig");

        properties.ShouldContain(
            "'$(MSBuildProjectName)' != 'SmartSentinelEye.AppHost'",
            Case.Sensitive,
            "AppHost is the sole project exemption (ADR-0139): it does not reference Shared.Kernel by "
            + "design, so Ensure is unavailable there. Widening this condition to exclude Shared.* or "
            + "test projects would quietly restore the exemption ADR-0139 deliberately removed — the "
            + "guard ban binds more broadly than ADR-0049's ConfigureAwait ban, on purpose.");

        editorconfig.ShouldContain(
            "[**/Migrations/*.cs]",
            Case.Sensitive,
            "generated migrations are exempted by path (ADR-0139), because an inline #pragma would not "
            + "survive `dotnet ef migrations add` regenerating the file. The analyzer's own "
            + "exclude_generated_code option does not cover them: no migration here carries "
            + "GeneratedCodeAttribute, so it would exempt the .Designer.cs companions and miss the "
            + "bodies, where every banned call actually lives.");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllText(path);
    }

    private static DirectoryInfo RepositoryRoot()
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
}
