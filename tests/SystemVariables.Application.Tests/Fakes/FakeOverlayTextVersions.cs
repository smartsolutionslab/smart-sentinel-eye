using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

/// <summary>
/// Hand-written in-memory fake for <see cref="IOverlayTextVersions"/>
/// (ADR-0054 — no AutoFixture). Mirrors the real store's cutover-floor
/// contract closely enough for handler and query-handler unit tests: the
/// first advance for an overlay returns <see cref="Floor"/>, never
/// <c>1</c>, which is what makes it possible to tell "the version came
/// from this store" apart from "the version came from the retired
/// per-process reverse-index counter" without reading the handler's
/// source (issue #2426).
/// </summary>
public sealed class FakeOverlayTextVersions : IOverlayTextVersions
{
    /// <summary>Spec 202 plan.md §3's cutover floor. Copied, not shared with
    /// production, so a test failure here can never be masked by the fake and
    /// the real store silently drifting to the same wrong constant.</summary>
    public const long Floor = 1_000_000_000;

    private readonly Dictionary<Guid, long> versions = [];

    /// <summary>
    /// Every <see cref="AdvanceAsync"/> call, in order, one entry per call —
    /// not per overlay. A fan-out over N overlays that calls this N times
    /// shows up as N entries here; the store contract requires exactly one.
    /// </summary>
    public List<IReadOnlyCollection<Guid>> AdvanceCalls { get; } = [];

    /// <summary>Every <see cref="CurrentAsync"/> call, in order.</summary>
    public List<Guid> CurrentAsyncCalls { get; } = [];

    /// <summary>
    /// Optional shared sequence recorder for call-order assertions (T004,
    /// Finding B): set this to a list also written to by a recording
    /// <c>IResolver</c>, and assert the merged order afterwards.
    /// </summary>
    public List<string>? CallOrder { get; set; }

    public Task<IReadOnlyDictionary<Guid, long>> AdvanceAsync(
        IReadOnlyCollection<Guid> overlayIdentifiers, CancellationToken cancellationToken)
    {
        AdvanceCalls.Add([.. overlayIdentifiers]);

        Dictionary<Guid, long> advanced = [];
        foreach (Guid overlayIdentifier in overlayIdentifiers.Distinct())
        {
            long next = versions.TryGetValue(overlayIdentifier, out long current) ? current + 1 : Floor;
            versions[overlayIdentifier] = next;
            advanced[overlayIdentifier] = next;
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, long>>(advanced);
    }

    public Task<long> CurrentAsync(Guid overlayIdentifier, CancellationToken cancellationToken)
    {
        CurrentAsyncCalls.Add(overlayIdentifier);
        CallOrder?.Add("VersionRead");

        return Task.FromResult(versions.TryGetValue(overlayIdentifier, out long version) ? version : 0);
    }

    /// <summary>
    /// Test seam: seeds a version directly, bypassing <see cref="AdvanceAsync"/>'s
    /// floor semantics, for a test that needs a known starting version rather
    /// than the floor.
    /// </summary>
    public void Seed(Guid overlayIdentifier, long version) => versions[overlayIdentifier] = version;
}
