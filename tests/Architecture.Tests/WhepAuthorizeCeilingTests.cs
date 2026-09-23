using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the production WHEP-authorize rate-limit ceiling —
/// <c>WhepAuthorizeRateLimiting:PermitLimit</c> / <c>:Window</c> in the one
/// shipped <see cref="SettingsFile"/> — against silent config drift (issue
/// #2515, spec 220, following spec 208 / #2284).
///
/// <para>
/// <b>The derivation, not just the number.</b> Spec 208 §*Sizing the ceiling*
/// computes a worst sustained minute of ≈800 authorize POSTs (100 concurrent
/// viewer sessions × ≈8 retry-ladder attempts in the worst 60 s) and sets the
/// ceiling at 2.5× that figure — 2000/min. #2284's phase-6 fixes added
/// <c>Ensure.That(...).AtLeast(1)</c> at <c>Program.cs:33-35</c>, which catches
/// an invalid ceiling (zero, negative) at startup. It does not catch a legal,
/// non-zero, *wrong* one: <c>200</c> binds, the host starts, every health check
/// is green, and the ceiling silently sits at a quarter of the computed worst
/// minute. This guard is that missing assertion.
/// </para>
///
/// <para>
/// <b>The category is the value here</b> — unlike
/// <see cref="ContainerImagePinTests"/>, <see cref="DatabaseCommandLogLevelTests"/>
/// and <see cref="DockerfileUpstreamPinTests"/>, which all ban a category and
/// name no value, because a guard that obstructs its own legitimate change gets
/// deleted within a month. There is no property of "a well-sized
/// <c>PermitLimit</c>" this guard could check without redoing spec 208's
/// derivation in code — encoding 100 sessions, an 8-attempt ladder and a 2.5×
/// margin here so that a change to any one of them fails a guard nobody can
/// read. The number is the conclusion of an argument that lives in prose; the
/// only honest guard pins the conclusion and points at the argument. The
/// deletion risk is answered by the failure message, not by declining to name a
/// value: an engineer who legitimately wants a different ceiling is told, in
/// the assertion, where 2000 came from and what else must change (the spec's
/// derivation) for a new number to be defensible — "update the spec and the
/// test together", not "delete the annoying test".
/// </para>
///
/// <para>
/// <b>What this does not govern.</b> <c>AppHost.cs</c>'s <c>isE2ETests</c>
/// override (<c>PermitLimit=50</c>, <c>Window=00:00:10</c>, sized for 27 known
/// integration-lane authorize consumers) is correct as written and out of
/// scope. <c>appsettings.Development.json</c> carries no such section and is
/// not read here — <c>appsettings.json</c> is the file that ships.
/// </para>
///
/// <para>
/// Reads <see cref="SettingsFile"/> from disk through
/// <see cref="ConfigurationBuilder"/>, exactly as <c>Program.cs:21-24</c> binds
/// it — the same key strings, the same generic arguments — so this guard
/// cannot disagree with the host about what the <b>file</b> says. Like
/// <see cref="ContainerImagePinTests"/> and <see cref="DatabaseCommandLogLevelTests"/>,
/// it does not load the Api assembly or boot a host: the file is the artifact,
/// and binding it is cheaper and more faithful than booting the process that
/// reads it.
/// </para>
///
/// <para>
/// <b>What "exactly as Program.cs binds it" does not cover.</b> A host binds
/// the full <c>builder.Configuration</c> composition — this file, then
/// <c>appsettings.{Environment}.json</c>, then environment variables, then the
/// command line, each superseding the last. This guard reads only the shipped
/// <b>file</b>, so it asserts what ships, not what a given host resolves at
/// runtime. Today exactly one thing supersedes it, deliberately and out of
/// scope: <c>AppHost.cs:441-442</c>'s <c>isE2ETests</c> block sets
/// <c>WhepAuthorizeRateLimiting__PermitLimit</c> / <c>__Window</c> as
/// environment variables for the integration lane. An env var or a Development
/// override introduced anywhere else would supersede this file at runtime
/// exactly as that one does, and this guard would not see it.
/// </para>
///
/// <para>
/// The second <c>[Fact]</c> (US2, #2515) reads <c>Program.cs</c>'s own
/// <c>?? 2000</c> / <c>?? TimeSpan.FromMinutes(1)</c> fallback literals as
/// **text**, with comments blanked via
/// <see cref="SourceMask.Apply(string, MaskStrictness)"/>
/// (<see cref="MaskStrictness.CommentsBlankedLiteralsIntact"/>) so a comment
/// that happens to quote the fallback pattern cannot flip the match count —
/// top-level statements compile with nothing reflectable, so this is the only
/// way to reach them. It checks the fallbacks agree with the bound value, so a
/// host started without the shipped file does not silently fall back to a
/// drifted number. A literal scan cannot see a fallback whose value comes from
/// a <c>const</c>, a different overload, or a helper method; if
/// <c>Program.cs</c> is reshaped that way, the match-count assertion fails
/// loudly rather than passing on a scan that stopped matching.
/// </para>
///
/// <para>
/// <b>The most likely future trigger.</b> Spec 208 §*Sizing the ceiling* names
/// its own open unknown: whether a WHEP open costs one authorize POST or two
/// (SC-005, unmeasured at the time of that spec). If a later measurement finds
/// two, the ceiling itself is expected to move — this guard's expected value
/// moves with it, and its failure message already says where to make that
/// change defensible.
/// </para>
/// </summary>
public class WhepAuthorizeCeilingTests
{
    private const string SettingsFile = "src/StreamDistribution/Api/appsettings.json";
    private const string ProgramFile = "src/StreamDistribution/Api/Program.cs";

    private const string PermitLimitKey = "WhepAuthorizeRateLimiting:PermitLimit";
    private const string WindowKey = "WhepAuthorizeRateLimiting:Window";

    private const int ExpectedPermitLimit = 2000;
    private static readonly TimeSpan ExpectedWindow = TimeSpan.FromMinutes(1);

    private const string Derivation =
        "spec 208 derives ≈800 authorize POST/min as the worst computed minute (100 concurrent "
        + "viewer sessions × ≈8 retry-ladder attempts in the worst 60 s) and sets the ceiling "
        + "at 2.5× that figure — see specs/208-a-ceiling-the-hook-never-had/spec.md "
        + "§Sizing the ceiling.";

    private static readonly Regex PermitLimitFallback = new(
        PermitLimitKey + @"""\)\s*\?\?\s*(?<value>\d+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex WindowFallback = new(
        WindowKey + @"""\)\s*\?\?\s*TimeSpan\.From(?<unit>\w+)\((?<value>\d+)\)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void The_shipped_whep_authorize_ceiling_is_the_number_spec_208_derived()
    {
        Ceiling ceiling = ReadShippedCeiling();

        ceiling.PermitLimit.ShouldBe(
            ExpectedPermitLimit,
            $"{SettingsFile}'s '{PermitLimitKey}' is {ceiling.PermitLimit}, expected "
            + $"{ExpectedPermitLimit}. {Derivation}");

        ceiling.Window.ShouldBe(
            ExpectedWindow,
            $"{SettingsFile}'s '{WindowKey}' is {ceiling.Window}, expected {ExpectedWindow}. "
            + $"{Derivation}");
    }

    [Fact]
    public void The_program_fallback_defaults_agree_with_the_shipped_ceiling()
    {
        Ceiling shipped = ReadShippedCeiling();
        string raw = File.ReadAllText(Path.Combine(RepositorySource.Root().FullName, ProgramFile));
        string text = SourceMask.Apply(raw, MaskStrictness.CommentsBlankedLiteralsIntact);

        MatchCollection permitLimitMatches = PermitLimitFallback.Matches(text);
        permitLimitMatches.Count.ShouldBe(
            1,
            $"expected exactly one '?? <int>' fallback for '{PermitLimitKey}' in {ProgramFile}, found "
            + $"{permitLimitMatches.Count}. A source scan that stops matching must be repointed, not "
            + "treated as a pass.");

        MatchCollection windowMatches = WindowFallback.Matches(text);
        windowMatches.Count.ShouldBe(
            1,
            $"expected exactly one '?? TimeSpan.From...(...)' fallback for '{WindowKey}' in "
            + $"{ProgramFile}, found {windowMatches.Count}. A source scan that stops matching must be "
            + "repointed, not treated as a pass.");

        int fallbackPermitLimit = int.Parse(
            permitLimitMatches[0].Groups["value"].Value,
            CultureInfo.InvariantCulture);
        TimeSpan fallbackWindow = ResolveTimeSpan(
            windowMatches[0].Groups["unit"].Value,
            double.Parse(windowMatches[0].Groups["value"].Value, CultureInfo.InvariantCulture));

        fallbackPermitLimit.ShouldBe(
            shipped.PermitLimit,
            $"{ProgramFile}'s '?? {fallbackPermitLimit}' fallback for '{PermitLimitKey}' disagrees "
            + $"with {SettingsFile}'s bound value {shipped.PermitLimit}. Both carriers must move "
            + "together.");

        fallbackWindow.ShouldBe(
            shipped.Window,
            $"{ProgramFile}'s '?? TimeSpan...' fallback for '{WindowKey}' ({fallbackWindow}) "
            + $"disagrees with {SettingsFile}'s bound value {shipped.Window}. Both carriers must move "
            + "together.");
    }

    /// <summary>
    /// Binds <see cref="SettingsFile"/> exactly as <c>Program.cs:21-24</c> does
    /// and returns the pair, failing with a message that distinguishes an
    /// absent section/key from a present-but-unparseable value (FR-004).
    /// </summary>
    private static Ceiling ReadShippedCeiling()
    {
        string path = Path.Combine(RepositorySource.Root().FullName, SettingsFile);
        File.Exists(path).ShouldBeTrue(
            $"expected {SettingsFile} at {path} — if it moved, update this guard rather than "
            + "deleting it.");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build();

        int? permitLimit = Read<int>(configuration, PermitLimitKey, "an integer");
        permitLimit.ShouldNotBeNull(
            $"{SettingsFile} has no readable '{PermitLimitKey}' — the 'WhepAuthorizeRateLimiting' "
            + "section or the 'PermitLimit' key is absent, empty, or nested somewhere this exact path "
            + $"does not reach. {Derivation}");

        TimeSpan? window = Read<TimeSpan>(configuration, WindowKey, "a TimeSpan");
        window.ShouldNotBeNull(
            $"{SettingsFile} has no readable '{WindowKey}' — the 'WhepAuthorizeRateLimiting' "
            + "section or the 'Window' key is absent, empty, or nested somewhere this exact path does "
            + $"not reach. {Derivation}");

        return new Ceiling(permitLimit.Value, window.Value);
    }

    /// <summary>
    /// <c>null</c> when the key is genuinely absent — an absent section, an
    /// absent key, the whole object deleted, or the pair moved to a file this
    /// guard does not read (spec AS4, AS5 read as this). A present-but-
    /// unparseable value (<c>"two thousand"</c>, spec AS7) is translated from
    /// the binder's own <see cref="InvalidOperationException"/> into a message
    /// naming the offending raw string, rather than a raw binder exception
    /// surfacing unexplained (no drive-by error handling — this is the one
    /// trust-boundary translation this guard makes).
    /// </summary>
    private static T? Read<T>(IConfigurationRoot configuration, string key, string kind)
        where T : struct
    {
        string? raw = configuration[key];
        if (raw is null)
        {
            return null;
        }

        try
        {
            return configuration.GetValue<T?>(key);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"'{key}' is present ('{raw}') in {SettingsFile} but could not be read as {kind}: "
                + exception.Message,
                exception);
        }
    }

    /// <summary>
    /// The <c>TimeSpan.From&lt;Unit&gt;(value)</c> factory the fallback regex
    /// captured, resolved generically rather than assuming <c>Minutes</c> —
    /// the shipped fallback today, but not a fact this guard should encode
    /// twice. An unrecognised unit fails loudly rather than silently matching
    /// nothing.
    /// </summary>
    private static TimeSpan ResolveTimeSpan(string unit, double value) => unit switch
    {
        "Milliseconds" => TimeSpan.FromMilliseconds(value),
        "Seconds" => TimeSpan.FromSeconds(value),
        "Minutes" => TimeSpan.FromMinutes(value),
        "Hours" => TimeSpan.FromHours(value),
        "Days" => TimeSpan.FromDays(value),
        _ => throw new InvalidOperationException(
            $"{ProgramFile}'s Window fallback uses TimeSpan.From{unit}(...), which this guard does not "
            + "resolve — add a case or repoint the regex."),
    };

    /// <summary>The bound production pair.</summary>
    private sealed record Ceiling(int PermitLimit, TimeSpan Window);
}
