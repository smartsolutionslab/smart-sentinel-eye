using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Lists registered cameras with client-controlled sort + pagination
/// per spec 001-register-camera FR-007a + FR-007b.
///
/// <para>
/// <c>Fabs</c> is the fabs the caller holds (spec 015 FR-005). A list spans all
/// of them when none is named — the deliberate asymmetry with the write path,
/// which must choose. A listing that refused a multi-fab operator would be
/// unusable for exactly the people it exists for.
/// </para>
///
/// <para>
/// <c>IncludeRetired</c> is spec 028 FR-007. Retired cameras are out of the
/// way by default because the listing answers "what is out there", and
/// hardware that has been removed is not. Required rather than defaulted: the
/// two callers both state it, and a silent <c>false</c> is the kind of default
/// that later reads as "retired cameras were never considered here".
/// </para>
///
/// <para>
/// <c>NameFragment</c> is spec 055. **Optional, and absent is not "match
/// nothing"** — a cleared search box must return the catalogue, not empty it.
/// A fragment that is only whitespace is the same as none.
/// </para>
///
/// <para>
/// It is a fragment of a name, <b>not a pattern</b>. An operator types words;
/// characters with meaning to the underlying match are matched literally, so a
/// camera called <c>50% Load</c> is found by typing <c>%</c> and a fragment of
/// <c>%</c> does not match everything. That is a trust boundary as much as a
/// usability one — this arrives over HTTP.
/// </para>
/// </summary>
public sealed record ListCamerasQuery(
    IReadOnlyList<FabIdentifier> Fabs,
    string Sort,
    string Order,
    int Offset,
    int Limit,
    bool IncludeRetired,
    string? NameFragment = null)
    : IQuery<Result<CameraListPageDto, ListCamerasError>>
{
    /// <summary>
    /// The fragment to match on, or <c>null</c> when there is nothing to match.
    ///
    /// <para>
    /// Trimmed here rather than at each use so "absent", "empty" and "spaces"
    /// cannot mean three different things in three places (FR-003, FR-007).
    /// </para>
    /// </summary>
    public string? TrimmedFragment =>
        string.IsNullOrWhiteSpace(NameFragment) ? null : NameFragment.Trim();
}

public static class ListCamerasDefaults
{
    public const string DefaultSort = "registeredAt";
    public const string DefaultOrder = "desc";
    public const int DefaultOffset = 0;
    public const int DefaultLimit = 50;

    /// <summary>
    /// The largest page this endpoint serves. It <b>refuses</b> anything larger
    /// — <c>CATALOG_LIMIT_EXCEEDED</c> — rather than clamping, so asking for more
    /// is an error and not a smaller page.
    ///
    /// <para>
    /// <b>This is a page size, not the deployment's 250-camera scale target</b>
    /// (ADR-0170; the target itself is per deployment, shared across its fabs,
    /// not per fab — ADR-0159). No single request enumerates the whole of a
    /// deployment regardless of this number: <c>includeRetired=true</c>
    /// accumulates past any target over a deployment's lifetime, and a caller
    /// holding several fabs sees their combined total. Every caller wanting the
    /// full set already pages — the picker in <c>apps/shared</c> and the
    /// scenario simulator's read-back both do.
    /// </para>
    /// </summary>
    public const int MaximumLimit = 200;
}
