using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Clears the measurement residue a run-mode stack has already accumulated
/// (#2004).
///
/// <para>
/// <b>A one-off, not a scheduled job.</b> <see cref="IngestSpanMeasurement"/>
/// now archives what it defines, so nothing new accumulates; this is for the
/// 1468 rows deposited before it did. It is idempotent, so running it twice
/// costs a listing and nothing else.
/// </para>
///
/// <para>
/// <b>Driven as a test because that is where the machinery already is.</b>
/// Reaching a run-mode stack means minting a token against the realm's issuer
/// and pointing at the proxied address, which <see cref="RunModeStackAddress"/>
/// does and a shell script would have to reimplement.
/// </para>
///
/// <para>
/// Excluded from CI for the same reason as its measurement siblings: it needs a
/// stack CI does not run, and it refuses rather than starting one.
/// </para>
/// </summary>
public class RunModeVariableResidueSweep(ITestOutputHelper output)
{
    [Trait("Category", "Maintenance")]
    [Fact]
    public async Task Archive_the_measurement_variables_a_run_mode_stack_still_holds()
    {
        Option<RunModeStackAddress> configured = RunModeStackAddress.FromEnvironment();
        configured.HasValue.ShouldBeTrue(RunModeStackAddress.Missing);

        RunModeStackAddress address = configured.Value;
        output.WriteLine($"endpoint reached                      : {address.Describe()}");

        using HttpClient variables = await address.CreateAuthenticatedClientAsync(CancellationToken.None);

        // The default listing, which since #2015 is the un-archived rows. So a
        // second run of this sweep finds nothing left to do rather than trying
        // to archive what it archived the first time.
        string[] residue = await ResidueAsync(variables);
        output.WriteLine($"residue found                         : {residue.Length}");

        int archived = await VariableRequests.ArchiveAllAsync(variables, residue, CancellationToken.None);
        output.WriteLine($"archived                              : {archived}");

        // Asked of the server again rather than inferred from the count, because
        // the count is what this sweep believes and the listing is what an
        // operator will actually open.
        string[] remaining = await ResidueAsync(variables);
        output.WriteLine($"residue remaining                     : {remaining.Length}");

        remaining.ShouldBeEmpty();
    }

    private static async Task<string[]> ResidueAsync(HttpClient variables)
    {
        HttpResponseMessage listed = await variables.GetAsync("/system-variables");
        listed.EnsureSuccessStatusCode();
        JsonElement payload = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return payload.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .Where(name => name is not null && name.StartsWith(IngestSpanMeasurement.VariablePrefix, StringComparison.Ordinal))
            .Select(name => name!)
            .ToArray();
    }
}
