namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// The one bound both halves of the reaper (#2290) read: how old an unfinished
/// reservation has to be before it belongs to nobody and the next caller may
/// take it over.
/// </summary>
public static class IdempotencyReclamation
{
    /// <summary>
    /// How long an unfinished reservation is honoured before it is treated as
    /// abandoned.
    ///
    /// <para>
    /// <b>Ten minutes, derived rather than guessed.</b> It has to sit far above
    /// any request this system can legitimately make, because the two ways to
    /// get it wrong are not symmetric: too long merely makes a client wait, but
    /// too short reclaims a reservation whose owner is still alive — the work
    /// then runs a second time while the first attempt is still in flight, which
    /// is the exact double-application ADR-0142 exists to prevent. So the bound
    /// is set with headroom, not tightness:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// 120x <see cref="IdempotentRequest"/>'s own in-progress wait — 10 polls of
    /// 500 ms (<c>InProgressPolls</c> x <c>InProgressPollInterval</c>) — the
    /// longest a live retry is ever kept waiting before that path gives up and
    /// answers 409.
    /// </item>
    /// <item>
    /// 60x the "10 s attempt timeout" <c>IdempotentRequest.cs</c>'s
    /// <c>InProgressPollInterval</c> comment names as the window the poll is
    /// sized to sit inside — the longest a single attempt is expected to take
    /// before something else (a load balancer, a client) would have already
    /// given up on it.
    /// </item>
    /// <item>
    /// The slowest legitimate <c>work()</c> among the ten keyed call sites —
    /// <c>POST /devices/register</c>, which creates a Keycloak client and reads
    /// its secret back — runs in low seconds under load, never minutes.
    /// </item>
    /// </list>
    /// <para>
    /// A reader who doubts the number can re-derive it from those three facts
    /// rather than take it on trust.
    /// </para>
    /// </summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromMinutes(10);
}
