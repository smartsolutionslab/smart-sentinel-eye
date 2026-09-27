using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Spec 270 (ADR-0160), plan.md §2 — the pure refusal rule shared by both
/// enforcement points (the nine REST APIs' <c>OnTokenValidated</c> hook and
/// <c>WhepAuthValidator</c>):
///
/// <code>
/// refuse(token) ⇔ azp ≠ null
///              ∧ snapshot.TryGet(azp, out latestDisabledAt)
///              ∧ (iat = null ∨ latestDisabledAt ≥ iat − 5 min)
/// </code>
///
/// Drives <see cref="RevokedClientSnapshot.Refuses"/> directly — no host, no
/// I/O, no clock dependency injected anywhere — because the rule is a pure
/// function over an immutable snapshot (plan.md §4.3).
///
/// <para>
/// <b>Red today: compile.</b> Neither <c>RevokedClientSnapshot</c> nor its
/// namespace exists on <c>develop</c>. The constructor shape asserted here —
/// <c>RevokedClientSnapshot.From(IReadOnlyList&lt;RevokedClientEntry&gt;)</c> —
/// is not in plan.md verbatim; only <c>Refuses(string?, DateTimeOffset?)</c>
/// is (plan.md §4.3). <c>.From(...)</c> is this repository's standing
/// value-object construction convention (ADR-0038/0046/0066), so it is the
/// contract this file hands the implementing engineer rather than a plan.md
/// quotation.
/// </para>
/// </summary>
public class RevokedClientSnapshotTests
{
    private const string RevokedClientId = "kiosk-269";

    private static readonly DateTimeOffset DisabledAt =
        new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_token_minted_before_the_disable_is_refused()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        snapshot.Refuses(RevokedClientId, DisabledAt.AddMinutes(-10)).ShouldBeTrue();
    }

    /// <summary>
    /// Plan.md §2's clock-skew tolerance: a token minted 4 min 59 s after
    /// <c>DisabledAt</c> is still within the 5-minute allowance the bearer
    /// pipeline already extends to <c>exp</c>, so it is refused rather than
    /// treated as a re-registration.
    /// </summary>
    [Fact]
    public void A_token_minted_within_the_five_minute_tolerance_after_the_disable_is_still_refused()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        DateTimeOffset justInsideTolerance = DisabledAt + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59);

        snapshot.Refuses(RevokedClientId, justInsideTolerance).ShouldBeTrue(
            "a token minted 4:59 after DisabledAt is within the 5-minute clock-skew tolerance "
            + "(ADR-0160 §1) and must still be refused.");
    }

    /// <summary>
    /// Just past the tolerance: the shape a re-registered client's first token
    /// takes (plan.md §2, spec.md's re-registration scenario).
    /// </summary>
    [Fact]
    public void A_token_minted_just_past_the_tolerance_is_admitted()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        DateTimeOffset justPastTolerance = DisabledAt + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);

        snapshot.Refuses(RevokedClientId, justPastTolerance).ShouldBeFalse(
            "a token minted more than 5 minutes after DisabledAt belongs to a client re-registered "
            + "under the same id (ADR-0160 §1) and must be admitted.");
    }

    [Fact]
    public void A_client_id_the_snapshot_does_not_list_is_admitted()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        snapshot.Refuses("kiosk-never-disabled", DisabledAt.AddMinutes(-10)).ShouldBeFalse();
    }

    /// <summary>
    /// No <c>azp</c> means the token was not minted by a
    /// <c>RegisteredClient</c> at all; the existing pipeline has already
    /// judged it (plan.md §2).
    /// </summary>
    [Fact]
    public void A_token_with_no_azp_is_admitted()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        snapshot.Refuses(azp: null, issuedAt: DisabledAt.AddMinutes(-10)).ShouldBeFalse();
    }

    /// <summary>
    /// Keycloak always emits <c>iat</c>; a listed client's token missing it
    /// is anomalous, and a listed client is exactly where anomalous must not
    /// pass (plan.md §2).
    /// </summary>
    [Fact]
    public void A_listed_clients_token_with_no_iat_is_refused()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        snapshot.Refuses(RevokedClientId, issuedAt: null).ShouldBeTrue();
    }

    /// <summary>
    /// Disable, re-register, disable again: the later <c>DisabledAt</c> wins
    /// (FR-001), so a token minted between the two disables is caught by the
    /// second one even though it postdates the first.
    /// </summary>
    [Fact]
    public void The_later_of_two_entries_for_the_same_client_id_wins()
    {
        DateTimeOffset firstDisable = DisabledAt;
        DateTimeOffset secondDisable = DisabledAt.AddDays(1);

        RevokedClientSnapshot snapshot = RevokedClientSnapshot.From([
            new RevokedClientEntry(RevokedClientId, firstDisable),
            new RevokedClientEntry(RevokedClientId, secondDisable),
        ]);

        DateTimeOffset mintedBetweenTheTwoDisables = firstDisable.AddHours(1);

        snapshot.Refuses(RevokedClientId, mintedBetweenTheTwoDisables).ShouldBeTrue(
            "the snapshot must keep the later DisabledAt for a client id disabled twice; a token "
            + "minted after the first disable and before the second is still refused by the second.");
    }

    [Fact]
    public void The_client_id_match_is_ordinal_and_case_sensitive()
    {
        RevokedClientSnapshot snapshot = SnapshotOf((RevokedClientId, DisabledAt));

        snapshot.Refuses("KIOSK-269", DisabledAt.AddMinutes(-10)).ShouldBeFalse(
            "the client id match must be ordinal and case-sensitive (plan.md §4.3); a "
            + "case-insensitive match would refuse a client that was never disabled.");
    }

    private static RevokedClientSnapshot SnapshotOf(params (string ClientId, DateTimeOffset DisabledAt)[] entries) =>
        RevokedClientSnapshot.From([
            .. entries.Select(entry => new RevokedClientEntry(entry.ClientId, entry.DisabledAt)),
        ]);
}
