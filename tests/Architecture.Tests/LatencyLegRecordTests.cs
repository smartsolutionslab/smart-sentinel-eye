namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards constitution §IV's record of which latency legs are built (spec 040,
/// issue 1714).
///
/// <para>
/// §VII's dashboard obligation is <b>conditional</b> on that table — "a leg not
/// yet built is not yet subject". So the table is not documentation about the
/// code; it is what decides whether an obligation exists. A row that is wrong
/// exempts a leg from a constitutional rule, silently.
/// </para>
///
/// <para>
/// That is not hypothetical. Two legs stood recorded as "no" and "partly" while
/// their code ran on every kiosk, because a search for video looked in
/// <c>apps/kiosk-web</c> and the capability lives in <c>apps/shared</c>. Four
/// documents agreed with each other and none had been checked against the code.
/// §IV's own warning sentence had predicted exactly this.
/// </para>
///
/// <para>
/// <b>The assertion is on the document</b>, unusually, because the document is
/// what was wrong. Nothing here reads the source of the legs themselves — that
/// is what a person does, and what the four documents were standing in for.
/// </para>
/// </summary>
public class LatencyLegRecordTests
{
    [Fact]
    public void The_kiosk_legs_are_recorded_as_built()
    {
        string constitution = ReadConstitution();

        constitution.ShouldNotContain("| SFU → kiosk decode | **no** |",
            customMessage: "the kiosk decodes video — CellPage renders CameraViewer, which owns the "
            + "<video> element and drives the peer connection. Recorded as unbuilt, this leg is "
            + "exempt from §VII by the clerical error §IV warns about (spec 040).");

        constitution.ShouldNotContain("| Overlay composite + render | **partly**",
            customMessage: "the overlay is composited onto the live frame — CameraViewer takes an "
            + "`overlay` prop and draws it over the video. Recorded as partly built, this leg is "
            + "exempt from §VII (spec 040).");
    }

    /// <summary>
    /// <b>SC-007.</b> Six legs currently sit in four different states, and the
    /// table's job is to keep them distinguishable.
    ///
    /// <para>
    /// A weaker check — "no leg says unbuilt" — would pass against a table that
    /// rounded three of them up to watched, which is the failure this feature
    /// exists to correct rather than repeat. Each vocabulary term is asserted
    /// present, so losing one is a failure rather than a tidy-up.
    /// </para>
    /// </summary>
    [Fact]
    public void The_record_still_distinguishes_every_state_a_leg_can_be_in()
    {
        string constitution = ReadConstitution();

        constitution.ShouldContain("in part",
            customMessage: "the decode leg is measured in part — a browser cannot see the SFU's "
            + "sending end per frame. A shared clock now exists (ADR-0128) but Chromium exposes no "
            + "send-to-arrival mapping, so the far end can only be estimated. Rounding that up to "
            + "measured would report a 120 ms budget met on its cheaper half.");

        constitution.ShouldContain("recorded, not yet readable",
            customMessage: "the event → overlay leg emits a number nobody can read (issue 1707). "
            + "§VII is half discharged for it and the column says so rather than rounding up.");

        constitution.ShouldContain("recorded, not yet observed",
            customMessage: "the presentation-buffer leg emits a number that reaches the sink, but "
            + "no person has read it off a running wall. Distinct from 'not yet readable', and "
            + "weaker — the code is tested, the wall is not.");
    }

    /// <summary>
    /// <b>The successor to this file's original assertion, and the same job.</b>
    ///
    /// <para>
    /// Until spec 045 this test pinned the presentation buffer as
    /// <c>(PTP) | **no**</c> — "the one genuinely unbuilt leg", which must stay
    /// recorded as such because *not yet subject* is a claim someone made
    /// rather than an absence. That leg is now built (ADR-0128), so the row
    /// legitimately changed and the old assertion had to go.
    /// </para>
    ///
    /// <para>
    /// <b>The risk did not go with it — it inverted.</b> The tempting overstatement
    /// is no longer "unbuilt" but "measured": the figure reaches the sink, every
    /// unit test is green, and flipping the column to <c>yes</c> would look like
    /// tidying up. Nobody has read it off a running wall. So the pin moves to the
    /// claim that can now be silently rounded up.
    /// </para>
    /// </summary>
    [Fact]
    public void The_presentation_buffer_is_not_claimed_measured_before_anyone_has_looked()
    {
        string constitution = ReadConstitution();

        constitution.ShouldContain(
            "| Presentation buffer (playout alignment) | yes | **recorded, not yet observed**",
            customMessage: "the leg is built, and its Measured column may only become 'yes' once "
            + "spec 045's T026 has been walked and a figure read in the sink. A passing unit suite "
            + "is not a §VII discharge — see specs/045-wall-shows-one-instant/verification.md §5.");

        constitution.ShouldNotContain("Presentation buffer (PTP)",
            customMessage: "ADR-0128 renamed the leg because the mechanism uses no PTP. A leg named "
            + "after a technology it does not use is the record that misleads the next reader, "
            + "which is the defect this whole file exists to catch.");
    }

    /// <summary>
    /// The warning sentence stays. It was right, and it can now point at an
    /// instance — which is worth more than a rule nobody has seen fire.
    /// </summary>
    [Fact]
    public void The_table_still_warns_about_itself()
    {
        ReadConstitution().ShouldContain("exempt itself from §VII by clerical error");
    }

    private static string ReadConstitution()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, ".specify", "memory", "constitution.md");
        File.Exists(path).ShouldBeTrue($"the constitution should be at {path}");
        return File.ReadAllText(path);
    }
}
