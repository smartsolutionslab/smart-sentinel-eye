using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Reads TypeScript/TSX source the way a rule about <em>prose</em> — a class
/// name, a Tailwind utility string — must read it: comments stripped, then
/// only the content of single-, double- or template-quoted string literals
/// kept, so a rule never trips on an identifier or on JSX structure.
///
/// <para>
/// Spec 268 (issue #2336) plan.md §5.1, T001: lifted, unchanged, out of
/// <c>SharedUiTokenUsageTests</c> (spec 257), which now calls
/// <see cref="StringLiteralContent"/> here instead of carrying its own copy.
/// <c>InteractionStateTests</c> (spec 268) uses the same reader for its own
/// facts, so the two guards agree on what counts as "the content a rule
/// applies to" rather than each drifting its own regex.
/// </para>
/// </summary>
internal static class TypeScriptSource
{
    /// <summary>
    /// The content of every single-, double- or template-quoted string literal
    /// in the file, comments stripped first.
    /// </summary>
    internal static string StringLiteralContent(string source)
    {
        string withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        string withoutComments = Regex.Replace(withoutBlockComments, @"//[^\n]*", string.Empty);

        List<string> literals = [];

        foreach (Match match in Regex.Matches(
                     withoutComments,
                     @"'(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*""|`(?:[^`\\]|\\.)*`",
                     RegexOptions.Singleline))
        {
            literals.Add(match.Value[1..^1]);
        }

        return string.Join('\n', literals);
    }
}
