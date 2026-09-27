namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Holds the current <see cref="RevokedClientSnapshot"/>, replaced wholesale
/// by <see cref="RevokedClientRefresher"/> on every successful fetch
/// (spec 270, ADR-0160 §2/§3).
///
/// <para>
/// Registered as a singleton. The field is a plain reference — not the
/// <c>Option&lt;RevokedClientSnapshot&gt;</c> plan.md §4.3 sketches — because
/// <c>Option&lt;T&gt;</c> is a value-type struct and C# refuses <c>volatile</c>
/// on anything but a reference type or a handful of primitives (CS0677). A
/// nullable reference gives the same "no snapshot yet" state with a type
/// <c>volatile</c> actually accepts, and <see langword="null"/> is exactly
/// FR-005's "before the first successful load" case.
/// </para>
/// </summary>
public sealed class RevokedClientRegistry : IRevokedClientRegistry
{
    private volatile RevokedClientSnapshot? current;

    /// <summary>
    /// Before any snapshot has loaded, admits everything — today's behaviour
    /// (FR-005). Read once, so a concurrent replace mid-call cannot leave this
    /// method looking at two different snapshots.
    /// </summary>
    public bool Refuses(string? azp, DateTimeOffset? issuedAt) => current?.Refuses(azp, issuedAt) ?? false;

    /// <summary>Called only by <see cref="RevokedClientRefresher"/>, on a successful fetch.</summary>
    internal void Replace(RevokedClientSnapshot snapshot) => current = snapshot;
}
