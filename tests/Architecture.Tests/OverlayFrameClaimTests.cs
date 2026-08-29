namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the withdrawal of the SLO's <em>frame-synced</em> claim (ADR-0129,
/// issue 1967).
///
/// <para>
/// The overlay is a DOM label layered over the <c>&lt;video&gt;</c>, not
/// composited into a frame, so it cannot be paired with the frame whose instant
/// it describes. Doing that needs a clock shared between the camera and the
/// event source — PTP hardware this system does not have. §IV claimed the
/// pairing anyway, and spec 045 quietly widened the gap by adding playout
/// buffer.
/// </para>
///
/// <para>
/// <b>Consistency checks, not text pins</b>, following
/// <see cref="FoundingDecisionRecordTests"/>: each reads the code <em>and</em>
/// the record, and fails only when they disagree. Compositing the overlay into
/// the frame would not fail this suite; doing it and leaving §IV saying the
/// opposite would.
/// </para>
/// </summary>
public class OverlayFrameClaimTests
{
    /// <summary>
    /// The claim and the rendering must agree. While the overlay is layered
    /// over the video rather than drawn into it, no document may say the two
    /// are frame-synchronised.
    /// </summary>
    [Theory]
    [InlineData(".specify/memory/constitution.md")]
    [InlineData("docs/adr/0000-initial-decisions.md")]
    [InlineData("CLAUDE.md")]
    public void No_document_claims_the_overlay_is_frame_synced(string relativePath)
    {
        bool compositedIntoFrame = OverlayIsDrawnIntoTheFrame();
        string text = ReadRepositoryFile(relativePath);

        // A withdrawal necessarily names the phrase it withdraws, so only an
        // unqualified claim counts. Sentences that explain the withdrawal are
        // the correction, not a relapse.
        bool claimsIt = text
            .Split('\n')
            .Any(line => line.Contains("frame-synced", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("not frame-synced", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("withdraw", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("Originally", StringComparison.Ordinal));

        claimsIt.ShouldBe(
            compositedIntoFrame,
            compositedIntoFrame
                ? $"the overlay is now drawn into the frame, so {relativePath} may claim frame "
                + "synchronisation again — update it. This guard pins the record against drift, "
                + "never against progress."
                : $"{relativePath} claims the overlay is frame-synced. It is a DOM label layered over "
                + "the video, and pairing it with a frame needs a shared camera/event clock that does "
                + "not exist (ADR-0129). Aged labels are not frame accuracy, and calling them that "
                + "swaps one overclaim for a subtler one.");
    }

    /// <summary>
    /// <b>The direction buffering moves the gap must stay written down.</b>
    ///
    /// <para>
    /// Spec 045 widened this gap by adding playout buffer and nobody noticed
    /// until a code reading found it. The paragraph exists so the next feature
    /// that adds buffer knows what it is doing — losing it would restore the
    /// exact blind spot that produced this ADR.
    /// </para>
    /// </summary>
    [Fact]
    public void The_record_says_which_way_buffering_moves_the_gap()
    {
        ReadRepositoryFile(".specify/memory/constitution.md").ShouldContain(
            "adds playout buffer",
            customMessage: "§IV must keep saying that adding buffer makes the picture older, and that "
            + "aged labels move with it. Spec 045 added buffer without knowing it widened this gap "
            + "(ADR-0129); deleting the paragraph restores that blind spot.");
    }

    /// <summary>
    /// <b>T005 — the guard permits a legitimate rewording.</b>
    ///
    /// <para>
    /// The check above matches the withdrawn phrase, not a fixed sentence, so
    /// §IV can be rewritten freely as long as it does not re-assert the claim.
    /// Exercised against synthetic text rather than asserted in prose, because
    /// spec 047 shipped a guard that made partial progress unrepresentable and a
    /// "T016" test so tautological it could not fail.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("End-to-end SLO: event arrival → overlay rendered ≤ 800 ms.", false)]
    [InlineData("A totally reworded budget section with no such claim at all.", false)]
    [InlineData("The overlay is **not frame-synced** and never was (ADR-0129).", false)]
    [InlineData("1.6.0 — §IV withdraws the frame-synced claim (ADR-0129).", false)]
    [InlineData("| 015 | ... Originally read: overlay rendered, frame-synced = ≤ 800 ms.", false)]
    [InlineData("End-to-end SLO: overlay rendered, frame-synced ≤ 800 ms.", true)]
    public void A_rewording_passes_and_only_a_restated_claim_fails(string line, bool expectedToBeAClaim)
    {
        bool claimsIt = line.Contains("frame-synced", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("not frame-synced", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("withdraw", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("Originally", StringComparison.Ordinal);

        claimsIt.ShouldBe(
            expectedToBeAClaim,
            "the guard must accept any wording that does not re-assert frame synchronisation — "
            + "including a full rewrite, an explicit withdrawal, and the preserved original text of "
            + "an amended row. A guard that failed on legitimate edits would be deleted, and the "
            + "correction would lose its protection with it.");
    }

    /// <summary>
    /// Whether the overlay is drawn into the video frame rather than layered
    /// over it. Today it is a positioned DOM element, which is why the claim was
    /// withdrawn — if that ever changes, the claim may return.
    /// </summary>
    private static bool OverlayIsDrawnIntoTheFrame()
    {
        string viewer = ReadRepositoryFile(
            Path.Combine("apps", "shared", "src", "ui", "composites", "CameraViewer.tsx"));

        // Drawing into the frame means a canvas or an encoded-frame transform.
        // A positioned label element sits over the picture rather than inside it,
        // which is layering and not compositing.
        return viewer.Contains("getContext('2d')", StringComparison.Ordinal)
            || viewer.Contains("drawImage(", StringComparison.Ordinal)
            || viewer.Contains("RTCRtpScriptTransform", StringComparison.Ordinal);
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
