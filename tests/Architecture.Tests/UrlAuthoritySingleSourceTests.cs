using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 389 / issue #2999, FR-001, adversarial (T050). The rule that refuses a raw '@' after the authority is
/// meant to be one predicate, <c>UrlAuthority.AtSignFollowsTheAuthority</c>, called by both write-side value
/// objects and by <c>UrlRedaction.ToOrigin</c>. A drift between copies would be invisible through the value
/// objects (a '\' row is refused on the userinfo rule before the predicate is asked), so the sharing itself is
/// asserted from source: each of the three callers names the shared member, only <c>UrlAuthority.cs</c> declares
/// it, and the authority terminator set and the authority locator appear as code nowhere else in <c>src</c> but
/// the recorded exception below.
///
/// <para>
/// The recorded exception is <c>AuditPayloadRedaction.BracketContentIsAddressOnly</c> (AuditObservability,
/// spec 387), which locates a userinfo with its own copy of both. It predates this spec and answers a different
/// question (where the host's bracket starts), and it cannot reference the internal terminator constant from
/// another assembly. Listed by name so that a third copy fails here, and so that removing that one requires
/// editing this list rather than passing silently.
/// </para>
/// </summary>
public class UrlAuthoritySingleSourceTests
{
    private const string Declaration = "src/Shared.Kernel/UrlAuthority.cs";

    private const string AuditCopy = "src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs";

    private static readonly Regex SharedCall = new(
        @"\bUrlAuthority\s*\.\s*AtSignFollowsTheAuthority\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex PredicateDeclaration = new(
        @"\bbool\s+AtSignFollowsTheAuthority\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TerminatorLiteral = new(
        @"""(?:[/?#]|\\\\){3,}""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TerminatorCharacterList = new(
        @"'[/?#]'\s*,\s*'[/?#]'\s*,\s*'[/?#]'",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AuthorityLocator = new(
        @"IndexOfAnyExcept\(\s*'(?:/|\\\\)'\s*,\s*'(?:/|\\\\)'\s*\)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData("src/CameraCatalog/Domain/Camera/RtspUrl.cs")]
    [InlineData("src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs")]
    [InlineData("src/Shared.Kernel/UrlRedaction.cs")]
    public void Each_caller_asks_the_shared_predicate(string path)
    {
        var source = Sources()[path];

        RepositorySource.ExecutableLines(source).ShouldContain(line => SharedCall.IsMatch(line));
    }

    [Fact]
    public void Only_UrlAuthority_declares_the_predicate()
    {
        FilesWhere(source => RepositorySource.ExecutableLines(source).Any(line => PredicateDeclaration.IsMatch(line)))
            .ShouldBe([Declaration]);
    }

    [Fact]
    public void The_terminator_set_is_spelled_out_only_where_it_is_declared_and_in_the_recorded_audit_copy()
    {
        FilesWhere(source => TerminatorLiteral.IsMatch(source) || RepositorySource.ExecutableLines(source).Any(line => TerminatorCharacterList.IsMatch(line)))
            .ShouldBe([AuditCopy, Declaration], ignoreOrder: true);
    }

    [Fact]
    public void The_authority_locator_is_written_only_in_UrlAuthority_and_the_recorded_audit_copy()
    {
        FilesWhere(source => RepositorySource.ExecutableLines(source).Any(line => AuthorityLocator.IsMatch(line)))
            .ShouldBe([AuditCopy, Declaration], ignoreOrder: true);
    }

    [Theory]
    [InlineData("var end = authority.IndexOfAny(\"/?#\\\\\");")]
    [InlineData("var end = authority.IndexOfAny(\"#?/\");")]
    [InlineData("var end = authority.IndexOfAny(['/', '?', '#', '\\\\']);")]
    public void A_copied_terminator_set_is_recognised(string line)
    {
        (TerminatorLiteral.IsMatch(line) || TerminatorCharacterList.IsMatch(line)).ShouldBeTrue();
    }

    [Fact]
    public void A_copied_authority_locator_is_recognised()
    {
        AuthorityLocator.IsMatch("var start = rest.IndexOfAnyExcept('\\\\', '/');").ShouldBeTrue();
    }

    [Fact]
    public void The_fix_text_that_names_the_terminators_is_not_mistaken_for_a_copy()
    {
        var message = "\"must not contain a raw '@' after the first '/', '?', '#' or '\\\\': the text\"";

        TerminatorLiteral.IsMatch(message).ShouldBeFalse();
        RepositorySource.ExecutableLines(message).Any(line => TerminatorCharacterList.IsMatch(line)).ShouldBeFalse();
    }

    private static List<string> FilesWhere(Func<string, bool> predicate) =>
        [.. Sources().Where(source => predicate(source.Value)).Select(source => source.Key).Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> Sources()
    {
        var root = RepositorySource.Root();

        return Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => RepositorySource.RelativePath(root, file))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .ToDictionary(
                path => path,
                path => File.ReadAllText(Path.Combine(root.FullName, path)),
                StringComparer.Ordinal);
    }
}
