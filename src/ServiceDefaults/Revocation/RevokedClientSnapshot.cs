using System.Collections.Frozen;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// The pure refusal rule spec 270 (ADR-0160) builds on, shared by both
/// enforcement points — the nine REST APIs' <c>OnTokenValidated</c> hook and
/// <c>WhepAuthValidator</c> — so it is proved once (plan.md §2, §4.3):
///
/// <code>
/// refuse(token) ⇔ azp ≠ null
///              ∧ snapshot.TryGet(azp, out latestDisabledAt)
///              ∧ (iat = null ∨ latestDisabledAt ≥ iat − 5 min)
/// </code>
///
/// <para>
/// Immutable and holds no I/O: it wraps a <see cref="FrozenDictionary{TKey,TValue}"/>
/// of the latest <c>DisabledAt</c> per client id, built once by
/// <see cref="From"/> from Identity's <c>GET /registered-clients/revoked</c>
/// response and swapped wholesale by <see cref="RevokedClientRefresher"/>.
/// </para>
/// </summary>
public sealed class RevokedClientSnapshot
{
    /// <summary>
    /// Keycloak stamps <c>iat</c> and Identity's <see cref="Shared.Kernel.IClock"/>
    /// stamps <c>DisabledAt</c> from different clocks. This equals the bearer
    /// pipeline's own <c>ClockSkew</c>, already extended to <c>exp</c>, so
    /// reusing it here adds no new assumption (ADR-0160 §1).
    /// </summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    private readonly FrozenDictionary<string, DateTimeOffset> latestDisabledAtByClientId;

    private RevokedClientSnapshot(FrozenDictionary<string, DateTimeOffset> latestDisabledAtByClientId)
    {
        this.latestDisabledAtByClientId = latestDisabledAtByClientId;
    }

    /// <summary>
    /// Builds a snapshot from Identity's listing, collapsing repeated client
    /// ids (disable, re-register, disable again) to the later
    /// <see cref="RevokedClientEntry.DisabledAt"/> — FR-001's "latest wins".
    /// </summary>
    public static RevokedClientSnapshot From(IReadOnlyList<RevokedClientEntry> entries)
    {
        Dictionary<string, DateTimeOffset> latest = new(StringComparer.Ordinal);
        foreach (RevokedClientEntry entry in entries)
        {
            if (!latest.TryGetValue(entry.ClientId, out DateTimeOffset current) || entry.DisabledAt > current)
            {
                latest[entry.ClientId] = entry.DisabledAt;
            }
        }

        return new RevokedClientSnapshot(latest.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// The rule itself. <c>azp</c> is matched ordinally and case-sensitively —
    /// it is the exact string Keycloak minted the client under, not a
    /// display name.
    /// </summary>
    public bool Refuses(string? azp, DateTimeOffset? issuedAt)
    {
        if (azp is null || !latestDisabledAtByClientId.TryGetValue(azp, out DateTimeOffset latestDisabledAt))
        {
            return false;
        }

        // A listed client's token with no iat is anomalous, and a listed
        // client is exactly where anomalous must not pass (plan.md §2).
        if (issuedAt is not { } iat)
        {
            return true;
        }

        return latestDisabledAt >= iat - ClockTolerance;
    }
}
