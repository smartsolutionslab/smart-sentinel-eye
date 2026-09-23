using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 221 (#2514), a follow-up to spec 208 (#2284): this fact used to live in
/// <c>tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs</c>,
/// under <c>[Collection(AspireCollection.Name)]</c>, paying the cost of booting
/// the whole Aspire stack to run an assertion that makes zero HTTP calls and only
/// reads <c>src/StreamDistribution/Api/Program.cs</c> as text. It needs no
/// Docker daemon and no booted stack — that is the entire point of the move, and
/// <c>IntegrationTestSelectionTests</c>' own class doc states the general defect
/// this corrects: a verdict that could be read in the cheap job and was silently
/// read in the expensive one instead.
///
/// <para>
/// Its masking is <see cref="CodeLines"/>/<see cref="IsComment"/> — mirroring
/// <c>ResilienceRegistrationTests</c>' own pattern — <b>deliberately, not
/// <c>SourceMask</c></b>. <c>CodeLines</c> drops whole comment lines and rejoins
/// the survivors, shifting every offset in the scanned text; <c>SourceMask</c>
/// blanks in place and preserves length — a different contract. Adopting
/// <c>SourceMask</c> here would be an uncharacterised behaviour change to the
/// scan, out of scope for this move (spec 221 <c>spec.md</c> §*Assumptions*, Q1).
/// </para>
///
/// <para>
/// <b>Substitution, not the original design — stated explicitly per spec 208
/// tasks.md T004's own fallback clause.</b> The original version of this test
/// (committed at <c>ba9c5080</c>) posted from a second, explicitly
/// <c>127.0.0.2</c>-bound outbound socket to observe two genuinely
/// distinguishable remote addresses land in separate partitions. A temporary
/// diagnostic probe (since removed) confirmed that does not hold in this
/// topology: <c>stream-distribution</c>'s HTTP endpoint runs behind Aspire's DCP
/// proxy in dev/test (<c>WithHttpEndpoint()</c>, default-proxied,
/// <c>AppHost.cs</c>), and the proxy terminates every inbound connection and
/// re-originates it from its own loopback socket — so <b>both</b> the fixture's
/// default client and a <c>127.0.0.2</c>-bound client arrive at Kestrel as
/// <c>remote=::1</c>. The two "sources" the original test constructed are not
/// actually distinguishable at the layer the rate limiter partitions on, once an
/// intervening proxy sits in front — the original doc comment assumed direct
/// connection, and that assumption is false here.
/// </para>
///
/// <para>
/// <b>Spec 208 tasks.md T004 pre-authorizes exactly this fallback</b>: "If
/// varying the source address from the test host proves impossible, assert the
/// partition-key shape instead and say so explicitly — do not silently drop the
/// scenario." This is that fallback, not a weakened assertion — it reads
/// <c>Program.cs</c>'s limiter registration and asserts the <i>design
/// property</i> the original scenario existed to rule out: the partition key is
/// built from the connection's remote address, not a fixed or global bucket, so
/// two different remote addresses necessarily land in two different partitions.
/// That is a fact about the registration, not an observation that requires two
/// addresses to actually reach Kestrel distinguishably — which, through this
/// proxy, they cannot.
/// </para>
///
/// <para>
/// <b>Not phase-4a red evidence, in either home.</b> Nothing about this shape
/// depends on the rate limiter being wired up, so it would pass equally before
/// or after spec 208's T007. It remains a standing design guard against a
/// global-bucket regression, not red evidence — spec 208 tasks.md T004 says so
/// explicitly, and neither this PR nor spec 208's presents it as satisfying the
/// phase-4a gate.
/// </para>
/// </summary>
public sealed class WhepAuthorizePartitionKeyTests
{
    /// <summary>
    /// US1 acceptance scenario 3 / FR-003 (spec 208). The scenario that
    /// distinguishes a per-source partition from the rejected global-bucket
    /// design — a design under which one anonymous caller could exhaust the
    /// whole window and deny MediaMTX itself (spec 208 plan.md §Partition key).
    /// </summary>
    [Fact]
    public void Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket()
    {
        string programSource = File.ReadAllText(
            Path.Combine(RepositorySource.Root().FullName, "src", "StreamDistribution", "Api", "Program.cs"));

        // S4 (spec 208 review): the raw source between the registration call
        // and the options factory carries Program.cs's own explanatory
        // comment lines (e.g. "// Source IP, not a global bucket ...
        // RemoteIpAddress ..."), so searching the raw text would keep passing
        // if the real key argument were reverted to a fixed literal while a
        // comment mentioning RemoteIpAddress stayed behind. Strip comment
        // lines first, mirroring ResilienceRegistrationTests.CodeLines/IsComment.
        string codeOnly = string.Join(Environment.NewLine, CodeLines(programSource));

        int policyRegistration = codeOnly.IndexOf(
            "AddPolicy(\"whep-authorize\"", StringComparison.Ordinal);
        policyRegistration.ShouldBeGreaterThanOrEqualTo(
            0,
            "Program.cs no longer registers a \"whep-authorize\" policy by that name — the "
            + "partition-key shape below cannot be checked against a registration that is not there.");

        int limiterCall = codeOnly.IndexOf(
            "RateLimitPartition.GetFixedWindowLimiter(", policyRegistration, StringComparison.Ordinal);
        limiterCall.ShouldBeGreaterThan(
            policyRegistration,
            "expected the \"whep-authorize\" policy to build its partition via "
            + "RateLimitPartition.GetFixedWindowLimiter.");

        int keyArgumentStart = limiterCall + "RateLimitPartition.GetFixedWindowLimiter(".Length;
        int keyArgumentEnd = codeOnly.IndexOf(
            "_ => new FixedWindowRateLimiterOptions", keyArgumentStart, StringComparison.Ordinal);
        keyArgumentEnd.ShouldBeGreaterThan(
            keyArgumentStart,
            "could not find the fixed-window options factory that follows the partition-key "
            + "argument — the registration's shape has moved.");

        string partitionKeyArgument = codeOnly[keyArgumentStart..keyArgumentEnd].Trim().TrimEnd(',').Trim();

        // FR-003: the key must vary with the caller's remote address, never a
        // fixed literal — a fixed key is exactly the rejected global-bucket
        // design that would let one anonymous caller exhaust MediaMTX's own
        // window (spec 208 plan.md §Partition key, spec §"One global bucket").
        partitionKeyArgument.ShouldContain(
            "context.Connection.RemoteIpAddress",
            Case.Sensitive,
            "the \"whep-authorize\" policy's partition-key argument was (comments stripped):"
            + $"{Environment.NewLine}{partitionKeyArgument}"
            + $"{Environment.NewLine}FR-003 requires it to be built from the connection's remote "
            + "address, not a fixed/global bucket.");

        // S4's explicit negative check: the positive assertion above would
        // already fail if the key reverted to a fixed literal that also
        // dropped the "RemoteIpAddress" substring — this makes that failure
        // mode a named check rather than an incidental one. A bare quoted
        // literal with no interpolation hole is, by shape alone, the
        // rejected global-bucket design, regardless of what it happens to be
        // named.
        BareLiteralShape.IsMatch(partitionKeyArgument).ShouldBeFalse(
            "the partition-key argument reads as a single fixed string literal with no interpolation "
            + $"hole: '{partitionKeyArgument}'. FR-003 requires the key to vary with the caller's "
            + "connection — a bare literal is exactly the rejected global-bucket design that would "
            + "let one anonymous caller exhaust MediaMTX's own window.");
    }

    /// <summary>
    /// Matches a complete double-quoted string literal — interpolated or
    /// not — with no <c>{</c> anywhere inside it, spanning the whole
    /// (trimmed) key argument. The current key,
    /// <c>$"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"</c>,
    /// contains a <c>{</c> before its closing quote and never matches; a
    /// regression to a fixed key such as <c>"whep-authorize-bucket"</c> would.
    /// </summary>
    private static readonly Regex BareLiteralShape = new(
        @"^\$?""[^{]*""$", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Mirrors <c>ResilienceRegistrationTests.IsComment</c>.</summary>
    private static bool IsComment(string line) =>
        line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    /// <summary>Mirrors <c>ResilienceRegistrationTests.CodeLines</c>.</summary>
    private static IEnumerable<string> CodeLines(string source) =>
        source.Split('\n').Where(line => !IsComment(line));
}
