using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 092 US1, the acceptance scenario <b>"the steady state says nothing"</b>
/// — the one behaviour change on this branch that shipped with no test.
///
/// <para>
/// <b>Why this is load-bearing and not tidiness.</b> The sweep used to log
/// <c>SweptKioskPrivileges(0, 0)</c> on every pass. Now that it runs on every
/// Identity start, and every realm that exists is empty of enrolled kiosks
/// (spec 092 §"The verdict"), that would be a line per restart saying nothing
/// happened — which trains an operator to skip the one that matters.
/// <c>StreamFabAttributionService</c> made the same call and wrote down the same
/// reason.
/// </para>
///
/// <para>
/// <b>And phase 5's evidence depends on it.</b> The only proof the pass runs at
/// boot is this log line, read out of a running Identity API with a residue
/// planted first. Invert the guard and every start emits the line whether or not
/// the sweep found anything, and phase 5's observation stops meaning anything —
/// with nothing on the branch to catch that. Hence a capturing logger: the five
/// pre-existing <c>KioskPrivilegeSweepTests</c> pass <c>NullLogger</c>, so no
/// assertion anywhere could see this.
/// </para>
///
/// <para>
/// <b>Colour: red, for the first of the two.</b> Silencing a log line is a
/// behaviour change, not a refactor, so characterisation would have been the
/// wrong obligation (ADR-0139, constitution §Testing).
/// <c>A_pass_that_finds_no_kiosk_says_nothing</c> was observed failing by
/// counterfactual — the guard replaced by <c>if (true)</c> — and the verbatim
/// output is quoted in the PR body. <c>A_pass_that_finds_a_kiosk_says_so_once</c>
/// passes on both sides of that counterfactual and is not the red: it is here so
/// the silence cannot be reached by deleting the line altogether, which would
/// satisfy the first test and destroy phase 5's only evidence.
/// </para>
///
/// <para>
/// The change itself shipped earlier on this branch typed <c>refactor</c>, which
/// was the wrong colour; this file is what phase 4a owed it, added at phase 6.
/// </para>
/// </summary>
public class KioskPrivilegeSweepSteadyStateTests
{
    /// <summary>The generator names the event after the log method.</summary>
    private const string CompletionLine = "SweptKioskPrivileges";

    [Fact]
    public async Task A_pass_that_finds_no_kiosk_says_nothing()
    {
        EnrolledKiosksKeycloakAdminClient keycloak = new();
        CapturingLogger<KioskPrivilegeSweep> logger = new();

        await new KioskPrivilegeSweep(keycloak, logger).SweepAsync(CancellationToken.None);

        keycloak.EnumerationAttempts.ShouldBe(
            1,
            "a pass that never asked the provider is silent for the wrong reason, and would "
            + "satisfy the assertion below without the guard existing at all");

        logger.Named(CompletionLine).ShouldBeEmpty(
            "this is what every Identity start in every environment that exists today looks "
            + "like — no client anywhere carries sse.kind. A completion line on each of them "
            + "trains an operator to skip the one that reports a residue, which is the only "
            + "evidence that the sweep ran at boot at all (spec 092 phase 5, step 8).");
    }

    [Fact]
    public async Task A_pass_that_finds_a_kiosk_says_so_once_and_names_the_count()
    {
        EnrolledKiosksKeycloakAdminClient keycloak = new("kiosk-residue-a");
        CapturingLogger<KioskPrivilegeSweep> logger = new();

        await new KioskPrivilegeSweep(keycloak, logger).SweepAsync(CancellationToken.None);

        IReadOnlyList<LoggedEntry> completions = logger.Named(CompletionLine);

        completions.Count.ShouldBe(
            1,
            "silencing the empty pass must not silence the pass that found something: the line "
            + "phase 5 reads out of a running Identity API is this one, and it is emitted once "
            + "per pass, not once per kiosk");

        completions[0].Message.Contains("1 of 1", StringComparison.Ordinal).ShouldBeTrue(
            "the count is what makes the line evidence. A line reporting zero would not be "
            + "distinguishable from the steady state that phase 5 plants a residue precisely "
            + $"to escape. The line read: {completions[0].Message}");
    }

    /// <summary>
    /// Spec 132 US1 (#2169) — <b>red</b>. The silence spec 092 bought is guarded on
    /// the count of kiosks, not the count of strips, so a realm holding an enrolled
    /// kiosk reports "stripped 1 of 1" on <i>every</i> start whether or not that
    /// start repaired anything. Phase 5 of spec 092 watched exactly this: the
    /// residue was stripped on one boot and the identical line appeared on the next.
    ///
    /// <para>
    /// <b>The fake cannot make this pass on its own, and that is the point.</b>
    /// <c>AlreadyStripped</c> states the scenario — an account with no direct role
    /// mappings — but the port returns <c>Task</c>, so the fact never reaches the
    /// sweep. The information exists one frame down and is discarded; this test
    /// fails until the port carries it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_pass_over_a_kiosk_that_holds_nothing_says_nothing()
    {
        EnrolledKiosksKeycloakAdminClient keycloak = new("kiosk-swept-on-an-earlier-boot");
        keycloak.AlreadyStripped.Add("kiosk-swept-on-an-earlier-boot");
        CapturingLogger<KioskPrivilegeSweep> logger = new();

        await new KioskPrivilegeSweep(keycloak, logger).SweepAsync(CancellationToken.None);

        keycloak.Stripped.ShouldBe(
            ["kiosk-swept-on-an-earlier-boot"],
            "reporting less must not repair less — the removal stays idempotent and stays "
            + "attempted, because an account that somehow regains the privilege loses it at "
            + "the next boot (spec 052, ADR-0134 §1)");

        logger.Named(CompletionLine).ShouldBeEmpty(
            "the line says 'stripped', and nothing was stripped. An operator who reads it on "
            + "every restart cannot tell it from the boot where twelve accounts genuinely lost "
            + "privileges, which is the signal spec 092 silenced the empty realm to create.");
    }

    /// <summary>
    /// Spec 132 US2 (#2169) — <b>red</b>, and the half that stops US1 from being
    /// satisfied by deleting the logging altogether. Today this reports
    /// <c>2 of 2</c>: the numerator is kiosks <i>reached</i>.
    ///
    /// <para>
    /// Asserted on the <b>structured fields</b> rather than the message text
    /// (ADR-0050). The fields are what an OTLP sink carries and what an operator
    /// queries by, and a substring match on the rendered message would be green over
    /// an entry whose <c>StrippedCount</c> field named a different number — the same
    /// gap spec 122 (#2166) found in this file's sibling.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_pass_that_strips_one_of_two_counts_only_what_changed()
    {
        EnrolledKiosksKeycloakAdminClient keycloak = new("kiosk-residue-a", "kiosk-already-clean");
        keycloak.AlreadyStripped.Add("kiosk-already-clean");
        CapturingLogger<KioskPrivilegeSweep> logger = new();

        await new KioskPrivilegeSweep(keycloak, logger).SweepAsync(CancellationToken.None);

        IReadOnlyList<LoggedEntry> completions = logger.Named(CompletionLine);

        completions.Count.ShouldBe(
            1, "one line per pass, not one per kiosk and not one per repair");

        completions[0].Field("StrippedCount").ShouldBe(
            "1",
            "one account lost a privilege on this pass. Counting the other one — reached, "
            + $"holding nothing, changed in no way — is the defect. The line read: {completions[0].Message}");

        completions[0].Field("KioskCount").ShouldBe(
            "2",
            "the population stays visible: '1 of 2' says one residue among two, which a bare "
            + "'1' does not. #2169 forbids fixing this by removing the count.");
    }
}
