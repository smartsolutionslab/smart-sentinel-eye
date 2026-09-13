using System.Diagnostics;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #2201 US-4 — the overlay-snapshot readiness wait, folded from three near-identical
/// copies (<c>NFR_VariableResolutionLatencyTests</c>, <c>TwoPlaceholdersInOneLabelTests</c>,
/// <c>ResolvedTextReachesItsFabTests</c>) into the one place duplicated integration-test
/// helpers already live, mirroring <see cref="VariableRequests"/> and
/// <see cref="OverlayRequests"/> in shape and doc-comment style.
///
/// <para>
/// Until the reverse index picks an overlay up, <c>GET /system-variables/snapshot</c>
/// answers 404 and the label still carries its literal placeholder.
/// <see cref="ResolvedTextAsync"/> tells the two states apart with <c>null</c> rather than
/// <see cref="string.Empty"/> — <c>string.Empty.Contains(anything non-empty)</c> answers
/// <c>false</c>, the same answer a fully resolved label gives, which is the whole of #2201.
/// </para>
/// </summary>
internal static class OverlaySnapshotReadiness
{
    private const int PollIntervalMs = 200;

    internal static Task<HttpResponseMessage> SnapshotAsync(
        HttpClient variables, Guid overlay, CancellationToken cancellationToken = default) =>
        variables.GetAsync($"/system-variables/snapshot?overlayIdentifier={overlay}", cancellationToken);

    /// <summary>
    /// The resolved text, or <c>null</c> when the snapshot did not answer 200 — the two are
    /// different states and the readiness wait must tell them apart (#2201).
    /// </summary>
    internal static async Task<string?> ResolvedTextAsync(
        HttpClient variables, Guid overlay, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage snapshot = await SnapshotAsync(variables, overlay, cancellationToken);
        if (!snapshot.IsSuccessStatusCode)
        {
            return null;
        }

        return ResolvedTextIn(await snapshot.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Polls until the snapshot answers 200 <b>and</b> <paramref name="variableName"/> no
    /// longer renders as its literal placeholder, delaying between polls. The timeout names
    /// the overlay and the variable, and distinguishes "never a 200" from "a 200 that stayed
    /// stale" — an unbooted index and a snapshot loop that stopped early otherwise look
    /// identical from here.
    /// </summary>
    internal static async Task WaitUntilResolvableAsync(
        HttpClient variables,
        Guid overlay,
        string variableName,
        int ceilingMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        string literal = $"{{{{{variableName}}}}}";
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? resolved = null;

        while (stopwatch.ElapsedMilliseconds < ceilingMs)
        {
            resolved = await ResolvedTextAsync(variables, overlay, cancellationToken);
            if (resolved is not null && !resolved.Contains(literal, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        throw new TimeoutException(
            $"Overlay {overlay} never resolved '{variableName}' within {ceilingMs} ms; "
            + $"the last snapshot was "
            + $"{(resolved is null ? "not a 200" : $"a 200 carrying '{resolved}'")}. "
            + "Either the reverse index never picked the overlay up, or the snapshot loop "
            + "never reached that name.");
    }

    internal static string ResolvedTextIn(string body)
    {
        using JsonDocument payload = JsonDocument.Parse(body);

        return payload.RootElement.GetProperty("resolvedText").GetString() ?? string.Empty;
    }
}
