using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 394 / issue #3023 item 2, FR-007, red. <c>Uri</c> throws <see cref="IndexOutOfRangeException"/> from
/// <c>Host</c> and <c>UserInfo</c> for some inputs it parses. The two camera URL value objects must therefore read
/// the parser only through <c>UrlAuthority.TryReadHost</c> and <c>UrlAuthority.TryReadUserInfo</c>, which catch it
/// once in one place, and make no <c>Uri.TryCreate</c> call or <c>.Host</c> / <c>.UserInfo</c> read of their own.
/// A structural guard, because a later edit that reads <c>uri.Host</c> directly would compile, pass the rows it was
/// written against, and bring the 500 back for the next unreadable shape.
/// </summary>
public class CameraUrlGuardedUriReadTests
{
    private static readonly Regex DirectTryCreate = new(
        @"\bUri\s*\.\s*TryCreate\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DirectHostRead = new(
        @"\.\s*Host\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DirectUserInfoRead = new(
        @"\.\s*UserInfo\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex GuardedHostRead = new(
        @"\bUrlAuthority\s*\.\s*TryReadHost\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex GuardedUserInfoRead = new(
        @"\bUrlAuthority\s*\.\s*TryReadUserInfo\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static TheoryData<string> ValueObjects =>
    [
        "src/CameraCatalog/Domain/Camera/RtspUrl.cs",
        "src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs",
    ];

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void The_value_object_makes_no_Uri_TryCreate_call_of_its_own(string path)
    {
        Code(path).ShouldNotContain(line => DirectTryCreate.IsMatch(line));
    }

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void The_value_object_reads_neither_Host_nor_UserInfo_of_a_Uri_directly(string path)
    {
        Code(path).ShouldNotContain(line => DirectHostRead.IsMatch(line) || DirectUserInfoRead.IsMatch(line));
    }

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void The_value_object_reads_the_host_through_the_guarded_reader(string path)
    {
        Code(path).ShouldContain(line => GuardedHostRead.IsMatch(line));
    }

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void The_value_object_reads_the_userinfo_through_the_guarded_reader(string path)
    {
        Code(path).ShouldContain(line => GuardedUserInfoRead.IsMatch(line));
    }

    [Theory]
    [InlineData("var uri = new Uri(value); return uri.Host.Length > 0;")]
    [InlineData("return uri.UserInfo.Length == 0;")]
    [InlineData("return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0;")]
    public void A_copied_direct_read_is_recognised(string line)
    {
        (DirectTryCreate.IsMatch(line) || DirectHostRead.IsMatch(line) || DirectUserInfoRead.IsMatch(line)).ShouldBeTrue();
    }

    [Fact]
    public void The_doc_comment_text_that_names_the_parser_is_not_mistaken_for_a_read()
    {
        var source = "/// <summary>Uri.TryCreate then uri.Host and uri.UserInfo can throw.</summary>\n"
            + "private const string Note = \"uri.Host and Uri.TryCreate( are named in a message\";\n";

        RepositorySource.ExecutableLines(source).ShouldNotContain(line =>
            DirectTryCreate.IsMatch(line) || DirectHostRead.IsMatch(line) || DirectUserInfoRead.IsMatch(line));
    }

    [Fact]
    public void A_guarded_call_is_not_mistaken_for_a_direct_read()
    {
        var line = "UrlAuthority.TryReadHost(url, out var host) && UrlAuthority.TryReadUserInfo(url, out var userInfo);";

        DirectHostRead.IsMatch(line).ShouldBeFalse();
        DirectUserInfoRead.IsMatch(line).ShouldBeFalse();
        GuardedHostRead.IsMatch(line).ShouldBeTrue();
        GuardedUserInfoRead.IsMatch(line).ShouldBeTrue();
    }

    private static List<string> Code(string path)
    {
        var root = RepositorySource.Root();
        var source = File.ReadAllText(Path.Combine(root.FullName, path));

        return [.. RepositorySource.ExecutableLines(source)];
    }
}
