namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the single registration of the standard resilience handler.
///
/// <para>
/// <c>AddServiceDefaults</c> applies <c>AddStandardResilienceHandler</c> to every
/// client through <c>ConfigureHttpClientDefaults</c>, and every host calls
/// <c>AddServiceDefaults</c>. A second call for a particular client therefore does
/// not strengthen anything: <c>AddHttpMessageHandler</c> appends, so the client
/// ends up with two resilience pipelines, one nested inside the other. Retries
/// multiply rather than add — four attempts become sixteen — and the client
/// carries two independent circuit breakers and two 30 s budgets.
/// </para>
///
/// <para>
/// Eight registrations had done exactly that: LayoutComposition's camera guard,
/// StreamDistribution's MediaMTX gateway and camera lookup, and five of the
/// Scenario Simulator's clients. The MediaMTX one mattered most, because
/// <c>StreamHealthWatcher</c> sweeps it every two seconds.
/// </para>
///
/// <para>
/// Nothing catches this at build time and nothing catches it at run time either:
/// a doubled pipeline is not an error, it is a slower and more patient client.
/// The failure is visible only as latency during an outage, which is precisely
/// when nobody is reading registration code. Hence a guard.
/// <c>ResilienceHandlerNestingTests</c> in ServiceDefaults.Tests is its
/// counterpart — it observes the nesting this test exists to prevent, so the
/// rule is not asserted on the strength of this comment alone.
/// </para>
///
/// <para>
/// Reads source rather than reflecting over assemblies for the same reason
/// <c>GuardBanWiringTests</c> does: DI registration order leaves no trace in IL
/// that a test can distinguish from a single registration.
/// </para>
/// </summary>
public class ResilienceRegistrationTests
{
    private const string Registration = "AddStandardResilienceHandler(";

    /// <summary>
    /// Forward slashes, and <see cref="ReadSources"/> normalises to match.
    /// <c>Path.GetRelativePath</c> returns the platform separator, so a literal
    /// written with backslashes passes on a Windows developer machine and fails
    /// on Linux CI — which is the worst direction for a guard to break, because
    /// it is green exactly where it is least likely to be looked at.
    /// </summary>
    private const string SoleRegistrationSite = "src/ServiceDefaults/Extensions.cs";

    [Fact]
    public void The_standard_resilience_handler_is_called_exactly_once_across_src()
    {
        string[] callers = ReadSources()
            .Where(file => CodeLines(file.Value).Any(line => line.Contains(Registration, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        callers.ShouldBe(
            [SoleRegistrationSite],
            "ServiceDefaults already applies the standard resilience handler to every HttpClient via "
            + "ConfigureHttpClientDefaults. A second call for one client nests a whole second pipeline "
            + "inside the first rather than replacing it, so its retries multiply (4 attempts become 16) "
            + "and it gains a second circuit breaker and a second total-request budget. If a client "
            + "genuinely needs a different schedule, configure the existing pipeline's options — do not "
            + "add another handler.");
    }

    /// <summary>
    /// The counterpart to the count. A registration that had drifted out of
    /// ServiceDefaults entirely would still satisfy "exactly one", and would leave
    /// every client that is not the one named in that call with no resilience at
    /// all — silently, because an unprotected client looks identical to a
    /// protected one until something downstream fails.
    /// </summary>
    [Fact]
    public void The_one_registration_is_the_one_that_reaches_every_client()
    {
        string extensions = ReadSources()[SoleRegistrationSite];

        string[] code = [.. CodeLines(extensions)];

        int defaults = Array.FindIndex(code, line => line.Contains("ConfigureHttpClientDefaults", StringComparison.Ordinal));
        int resilience = Array.FindIndex(code, line => line.Contains(Registration, StringComparison.Ordinal));

        defaults.ShouldBeGreaterThanOrEqualTo(0,
            "AddServiceDefaults is expected to configure HttpClient defaults; without "
            + "ConfigureHttpClientDefaults the resilience handler below reaches whichever single client "
            + "it was attached to and no other.");

        resilience.ShouldBeGreaterThan(defaults,
            "the standard resilience handler is expected inside the ConfigureHttpClientDefaults callback, "
            + "which is what makes it apply to every client in every host. Moved out of that callback it "
            + "would protect one client and leave the rest bare while this file still, at a glance, looks "
            + "like it sets a global default.");
    }

    /// <summary>
    /// Lines with the comment prefix stripped out. The registration is named in
    /// prose in a few places — including in the comment explaining why it must not
    /// be called twice — and a guard that counted those would fail for describing
    /// itself.
    /// </summary>
    private static IEnumerable<string> CodeLines(string source) =>
        source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static Dictionary<string, string> ReadSources()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(
                f => Path.GetRelativePath(root.FullName, f).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);
    }
}
