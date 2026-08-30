using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the claims spec 049 corrected about how a kiosk comes back (ADR-0131).
///
/// <para>
/// <b>Consistency checks, not text pins.</b> Each asserts the record and the
/// code agree, and fails in either direction — so rewording is free and only a
/// divergence breaks the build. A guard that pins prose blocks legitimate edits,
/// gets deleted within a month, and takes the useful part with it.
/// </para>
///
/// <para>
/// These exist because ADR-0080 said for two years that the kiosk does
/// <b>not</b> use the interactive OIDC library while the kiosk used exactly
/// that, and nothing noticed until a feature attempted the decision. It is the
/// third such case in three features (ADR-014, ADR-021). A decision nobody has
/// attempted is a decision nobody has tested.
/// </para>
/// </summary>
public class KioskRecoveryRecordTests
{
    /// <summary>
    /// The kiosk keeps its tokens somewhere that outlives the browser process,
    /// and the record says so. Process-bound storage is why a reboot lost
    /// everything regardless of any server setting.
    /// </summary>
    [Fact]
    public void The_record_and_the_kiosk_agree_on_whether_tokens_survive_a_restart()
    {
        bool survivesRestart = KioskAuthSource().Contains("localStorage", StringComparison.Ordinal);

        ReadAdr0131().Contains("keeps it across restarts", StringComparison.OrdinalIgnoreCase)
            .ShouldBe(
                survivesRestart,
                survivesRestart
                    ? "the kiosk persists its grant across a restart; ADR-0131 should still say so"
                    : "ADR-0131 claims the grant survives a restart, and the kiosk no longer persists it");
    }

    /// <summary>
    /// <b>Authority must not have grown.</b> Unattended recovery is not bought
    /// with a broader grant, and this is the cheapest place to notice if it ever
    /// is — a reviewer would have to reason about a flow, a test just looks.
    /// </summary>
    [Fact]
    public void The_kiosk_asks_for_no_authority_beyond_coming_back()
    {
        string source = KioskAuthSource();
        Match scope = Regex.Match(source, @"scope:\s*'(?<scope>[^']*)'");

        scope.Success.ShouldBeTrue("the kiosk should declare the scopes it requests");

        string[] requested = scope.Groups["scope"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // `openid` is the sign-in itself and `offline_access` is what lets the
        // grant outlive the ten-hour ceiling. Anything else would be new
        // authority arriving under cover of a recovery feature — the six sse.*
        // scopes are DEFAULT client scopes and are never requested by name.
        // Bounded above: nothing beyond these two, or new authority has arrived
        // under cover of a recovery feature.
        requested.ShouldBeSubsetOf(
            ["openid", "offline_access"],
            "a recovery feature must not widen what a wall-mounted screen may do");

        // And bounded below. A subset check alone passes when a scope is
        // *removed* — dropping the long-lived grant would silently restore the
        // ten-hour ceiling and every test still went green. Found by mutation,
        // which is the only reason this line exists.
        requested.ShouldContain(
            "offline_access",
            "without it the sign-in session ends on the ten-hour clock and the wall drops out twice a day");
    }

    /// <summary>
    /// ADR-0131 must keep stating what the change costs, not only what it
    /// builds.
    ///
    /// <para>
    /// This is the one claim worth pinning, and deliberately so: the whole
    /// feature makes a credential last longer, and an ADR that records the
    /// mechanism while losing the trade hands the next reader a weakened
    /// posture with no note that anyone chose it. The check is loose — that the
    /// exposure is discussed at all — rather than a quotation, so the wording
    /// stays free.
    /// </para>
    /// </summary>
    [Fact]
    public void The_decision_still_records_what_it_gave_up()
    {
        string adr = ReadAdr0131();

        adr.ShouldContain("powered off", Case.Insensitive);
        adr.ShouldContain("yields a usable grant", Case.Insensitive);
    }

    /// <summary>
    /// The amended ADR keeps its original text legible rather than overwriting
    /// it. What was decided and what happened are two records, and losing the
    /// first loses the reason the second was needed.
    /// </summary>
    [Fact]
    public void The_superseded_kiosk_flow_is_still_readable_where_it_was_decided()
    {
        string adr0080 = ReadRepositoryFile(Path.Combine("docs", "adr", "0080-browser-auth.md"));

        adr0080.ShouldContain("Amended by ADR-0131");
        adr0080.ShouldContain(
            "bootKioskToken",
            Case.Sensitive);
    }

    /// <summary>
    /// The availability entry states the constraint that made the old claim
    /// wrong: the enrolment credentials exist and cannot be used from a browser.
    ///
    /// <para>
    /// <b>Asserted on the claim, not on a phrase.</b> The first version of this
    /// test forbade the words "smaller than it looks" anywhere in the entry —
    /// and failed immediately, because the correction <em>quotes</em> the old
    /// wording in order to say it was wrong. That is precisely the defect this
    /// file's own summary warns about: a guard that pins prose obstructs
    /// legitimate edits. Recorded rather than quietly fixed, because writing the
    /// warning did not stop me writing the guard it warns against.
    /// </para>
    /// </summary>
    [Fact]
    public void The_availability_target_records_why_the_credentials_cannot_be_used()
    {
        string constitution = ReadRepositoryFile(Path.Combine(".specify", "memory", "constitution.md"));

        int availability = constitution.IndexOf("20 kiosks rebooting", StringComparison.Ordinal);
        availability.ShouldBeGreaterThan(-1, "the availability target should still be stated");

        string entry = constitution[availability..Math.Min(constitution.Length, availability + 1400)];

        entry.ShouldContain("cannot be used from a browser", Case.Insensitive);
    }

    private static string KioskAuthSource() =>
        ReadRepositoryFile(Path.Combine("apps", "kiosk-web", "src", "app", "auth.ts"));

    private static string ReadAdr0131() =>
        ReadRepositoryFile(Path.Combine("docs", "adr", "0131-a-kiosk-keeps-its-own-grant.md"));

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, relativePath);
        File.Exists(path).ShouldBeTrue($"the guarded file should be at {path}");
        return File.ReadAllText(path);
    }
}
