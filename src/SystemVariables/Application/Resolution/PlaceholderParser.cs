using System.Text.RegularExpressions;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// Extracts <c>{{name}}</c> placeholder tokens from overlay label text
/// (spec 005 FR-009). Grammar matches VariableName exactly:
/// <c>^[A-Za-z][A-Za-z0-9_]{0,63}$</c>. Anything inside <c>{{...}}</c>
/// that doesn't match the grammar (whitespace, invalid first char,
/// too long) is left literal.
///
/// Pure static methods. No state, no I/O, fully unit-testable.
/// </summary>
public static partial class PlaceholderParser
{
    [GeneratedRegex(@"\{\{(?<name>[A-Za-z][A-Za-z0-9_]{0,63})\}\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Returns every unique variable name referenced by the given
    /// label text. Order is the first-occurrence order.
    /// </summary>
    public static IReadOnlyCollection<string> ExtractNames(string labelText)
    {
        ArgumentNullException.ThrowIfNull(labelText);
        if (labelText.Length == 0) return Array.Empty<string>();

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> ordered = new();
        foreach (Match m in PlaceholderRegex().Matches(labelText))
        {
            string name = m.Groups["name"].Value;
            if (seen.Add(name)) ordered.Add(name);
        }
        return ordered;
    }

    /// <summary>
    /// Substitutes every well-formed placeholder using
    /// <paramref name="resolveOne"/>. The resolver returns
    /// <c>null</c> for unknown/unset/archived names; the parser then
    /// keeps the literal <c>{{name}}</c> in the output.
    /// </summary>
    public static string Substitute(string labelText, Func<string, string?> resolveOne)
    {
        ArgumentNullException.ThrowIfNull(labelText);
        ArgumentNullException.ThrowIfNull(resolveOne);

        return PlaceholderRegex().Replace(labelText, m =>
        {
            string name = m.Groups["name"].Value;
            string? value = resolveOne(name);
            return value ?? m.Value;
        });
    }
}
