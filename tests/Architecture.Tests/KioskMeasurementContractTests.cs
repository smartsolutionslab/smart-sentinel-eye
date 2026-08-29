using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards that the kiosk and the server agree on which measurement names exist
/// (spec 046 T012).
///
/// <para>
/// <b>This set is a contract split across two languages, and one half fails
/// silently.</b> The browser's reporter swallows every failure by design — a
/// kiosk that cannot report its latency must carry on showing video. The server
/// returns a 400 for a name it does not know. So a name added to the client
/// alone produces a kiosk that reports nothing, logs nothing an operator sees,
/// and looks entirely healthy; the measurement simply never appears on a
/// dashboard, which is indistinguishable from a quiet fab.
/// </para>
///
/// <para>
/// A consistency check, not a text pin: it compares the two sides to each other
/// rather than to a list written here. Adding a measurement to both sides keeps
/// it green, which is what stops a guard like this being deleted the first time
/// it obstructs real work.
/// </para>
/// </summary>
public class KioskMeasurementContractTests
{
    /// <summary>
    /// The core claim. Every name one side knows, the other knows too.
    /// </summary>
    [Fact]
    public void The_kiosk_and_the_server_accept_the_same_measurement_names()
    {
        HashSet<string> client = ClientMeasurements();
        HashSet<string> server = ServerMeasurements();

        client.ShouldNotBeEmpty("the client union should have parsed");
        server.ShouldNotBeEmpty("the server's accepted names should have parsed");

        server.Except(client).ShouldBeEmpty("the server accepts a name no kiosk can send");
        client.Except(server).ShouldBeEmpty(
            "a kiosk sends a name the server refuses, and the kiosk swallows the refusal");
    }

    /// <summary>
    /// The validation message names every accepted value, so a 400 tells a
    /// caller what to send instead of only that it was wrong.
    ///
    /// <para>
    /// Checked because the message is hand-written prose beside a
    /// <c>switch</c>, and prose does not fail to compile when a case is added
    /// above it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_refusal_names_every_value_it_would_have_accepted()
    {
        string endpoints = ReadEndpoints();
        string message = Regex.Match(endpoints, @"""must be '.*?'""", RegexOptions.Singleline).Value
            + Regex.Match(endpoints, @"\+ ""'.*?'""", RegexOptions.Singleline).Value;

        message.ShouldNotBeEmpty("the validation message should have parsed");

        foreach (string measurement in ServerMeasurements())
        {
            message.ShouldContain($"'{measurement}'", Case.Sensitive);
        }
    }

    /// <summary>
    /// Names that are deliberately <b>not</b> latency segments are routed to
    /// their own instrument, and this checks the routing exists rather than the
    /// reasoning — the reasoning lives with each instrument.
    ///
    /// <para>
    /// Recording either as a segment would compile and pass every other test,
    /// which is precisely why it is checked here (ADR-0128, ADR-0129).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("wall_skew", "WallSkew.Record")]
    [InlineData("label_delay", "LabelDelay.Record")]
    public void A_name_that_is_not_a_segment_reaches_its_own_instrument(string measurement, string recorder)
    {
        string endpoints = ReadEndpoints();

        endpoints.ShouldContain($"\"{measurement}\"");
        endpoints.ShouldContain(recorder);

        // The segment switch must not also claim it. A name mapped to a
        // LatencySegment *and* routed to its own instrument would record twice
        // and read as a leg on one of the two.
        Regex.IsMatch(endpoints, $@"""{measurement}""\s*=>\s*LatencySegment\.")
            .ShouldBeFalse($"{measurement} is not a leg or a fragment of one");
    }

    /// <summary>
    /// Parses the client's closed union. Read from the source rather than
    /// duplicated here, so the test cannot agree with a stale copy of itself.
    /// </summary>
    private static HashSet<string> ClientMeasurements()
    {
        string source = ReadRepositoryFile("apps", "shared", "src", "observability", "kioskLatency.ts");
        Match declaration = Regex.Match(
            source,
            @"export type KioskMeasurement\s*=(?<body>.*?);",
            RegexOptions.Singleline);

        declaration.Success.ShouldBeTrue("the KioskMeasurement union should be declared");

        return Regex.Matches(declaration.Groups["body"].Value, @"'(?<name>[a-z_]+)'")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses what the server accepts: the names mapped to a latency segment,
    /// plus those compared directly because they route elsewhere.
    /// </summary>
    private static HashSet<string> ServerMeasurements()
    {
        string source = ReadEndpoints();

        IEnumerable<string> segments = Regex
            .Matches(source, @"""(?<name>[a-z_]+)""\s*=>\s*LatencySegment\.")
            .Select(match => match.Groups["name"].Value);

        IEnumerable<string> routed = Regex
            .Matches(source, @"report\.Measurement\s*==\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value);

        return segments.Concat(routed).ToHashSet(StringComparer.Ordinal);
    }

    private static string ReadEndpoints() =>
        ReadRepositoryFile("src", "StreamDistribution", "Api", "StreamEndpoints.cs");

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine([root.FullName, .. segments]);
        File.Exists(path).ShouldBeTrue($"the guarded file should be at {path}");
        return File.ReadAllText(path);
    }
}
