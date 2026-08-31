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

        // The phrase is the claim itself, not decoration: this is the sentence a
        // reader would rely on. It changed once already when the decision was
        // narrowed, and this check is what noticed.
        ReadAdr0131().Contains("survives the browser process", StringComparison.OrdinalIgnoreCase)
            .ShouldBe(
                survivesRestart,
                survivesRestart
                    ? "the kiosk persists its grant across a restart; ADR-0131 should still say so"
                    : "ADR-0131 claims the grant survives a restart, and the kiosk no longer persists it");
    }

    /// <summary>
    /// <b>Authority must not have grown by accident.</b> Unattended recovery is
    /// not bought with a broader grant, and this is the cheapest place to notice
    /// if it ever is — a reviewer would have to reason about a flow; a test just
    /// looks.
    ///
    /// <para>
    /// <b>This guard was rewritten rather than deleted, and the reason matters.</b>
    /// Its previous form asserted the kiosk requested <c>openid</c> and nothing
    /// else, and said that adding a long-lived grant back "should have to argue
    /// with a test". Spec 052 argued: a wall display genuinely does need one, or
    /// a wall drops to a prompt twice a day forever. So the boundary moved, in
    /// the open, and the guard now pins where it moved <i>to</i> — which is a
    /// stronger claim than the old one, because it fails if the long-lived scope
    /// ever appears next to the ordinary kiosk client.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_a_wall_display_asks_for_authority_beyond_coming_back()
    {
        string source = KioskAuthSource();

        MatchCollection pairs = Regex.Matches(
            source,
            @"clientId:\s*'(?<client>[^']*)'\s*,\s*scope:\s*'(?<scope>[^']*)'");

        pairs.Count.ShouldBe(2, "a screen is either a wall display or it is not, and each names its own client");

        foreach (Match pair in pairs)
        {
            string client = pair.Groups["client"].Value;
            string[] requested = pair.Groups["scope"].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (client == "kiosk-wall")
            {
                // The one screen that may hold a grant outliving its session.
                requested.ShouldBe(["openid", "offline_access"]);
                continue;
            }

            // Everything else: exactly the sign-in. The six sse.* scopes are
            // DEFAULT client scopes and are never requested by name, so anything
            // appearing here is new authority arriving under cover of a feature.
            requested.ShouldBe(["openid"]);

            // **The combination that locks everyone out.** An optional scope
            // refuses nobody only while nobody asks for it: an account without
            // the matching privilege that requests it is refused the entire
            // sign-in. On the ordinary kiosk client that is every operator.
            requested.ShouldNotContain(
                "offline_access",
                $"'{client}' asking for a long-lived grant refuses every account that lacks the privilege");
        }
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

        // Bounded by the next section rather than a character count. A fixed
        // window silently stops covering the entry the moment the entry grows,
        // which is exactly what happened when spec 050 rewrote it — the claim
        // was still there and the guard could no longer see it.
        int nextSection = constitution.IndexOf("### ", availability, StringComparison.Ordinal);
        string entry = nextSection < 0
            ? constitution[availability..]
            : constitution[availability..nextSection];

        // Whitespace-normalised before matching. The phrase is prose in a wrapped
        // markdown paragraph, so a reflow splits it across a line break and a raw
        // substring check fails on a document that still says exactly this. A guard
        // that breaks when someone rewraps a paragraph is the prose-pinning defect
        // this file warns about, in its subtlest form.
        string flattened = Regex.Replace(entry, @"\s+", " ");

        flattened.ShouldContain("cannot be used from a browser", Case.Insensitive);
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
