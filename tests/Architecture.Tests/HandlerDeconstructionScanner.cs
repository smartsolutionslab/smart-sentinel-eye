using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

internal sealed record HandlerDeconstructionScan(
    int Checked,
    int CheckedParenthesised,
    IReadOnlyList<string> Failures);

internal static class HandlerDeconstructionScanner
{
    private static readonly Regex HandlerSignature = new(
        @"public\s+async\s+Task[^(\n]*?\s(?:Handle|HandleAsync)\s*\(\s*"
        + @"([A-Za-z0-9_]+)\s+(\w+)\s*,",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Deconstruction = new(
        @"(?:var\s*\((?<plain>[^)]*)\)"
        + @"|\((?<paren>\s*(?:var\s+\w+|_)\s*(?:,\s*(?:var\s+\w+|_)\s*)+)\))"
        + @"\s*=\s*(?<target>\w+);",
        RegexOptions.Compiled);

    private static readonly Regex VarPrefix = new(@"^var\s+", RegexOptions.Compiled);

    private sealed record Resolution(IReadOnlyList<IReadOnlyList<string>>? Candidates, string? Problem);

    private sealed class ScanState(IReadOnlyDictionary<string, string> sources)
    {
        public IReadOnlyDictionary<string, string> Sources { get; } = sources;

        public Dictionary<(string Context, string Name), Resolution> Records { get; } = [];

        public List<string> Failures { get; } = [];

        public int Checked { get; set; }

        public int CheckedParenthesised { get; set; }
    }

    internal static HandlerDeconstructionScan Scan(IReadOnlyDictionary<string, string> sources)
    {
        ScanState state = new(sources);

        foreach ((var path, var text) in sources)
        {
            foreach (Match signature in HandlerSignature.Matches(text))
            {
                var body = BodyAfter(text, signature.Index);
                if (body is not null)
                {
                    CheckBody(path, signature, body, state);
                }
            }
        }

        return new HandlerDeconstructionScan(state.Checked, state.CheckedParenthesised, state.Failures);
    }

    private static void CheckBody(string path, Match signature, string body, ScanState state)
    {
        var typeName = signature.Groups[1].Value;
        var parameter = signature.Groups[2].Value;

        foreach (Match deconstruction in Deconstruction.Matches(body))
        {
            if (deconstruction.Groups["target"].Value != parameter)
            {
                continue;
            }

            state.Checked++;
            var parenthesised = deconstruction.Groups["paren"].Success;
            if (parenthesised)
            {
                state.CheckedParenthesised++;
            }

            var bound = BoundNames(deconstruction, parenthesised);
            var resolution = Resolve(path, typeName, state.Sources, state.Records);
            state.Failures.AddRange(Verify(path, typeName, bound, resolution));
        }
    }

    private static string[] BoundNames(Match deconstruction, bool parenthesised)
    {
        var list = parenthesised ? deconstruction.Groups["paren"].Value : deconstruction.Groups["plain"].Value;
        return list
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => parenthesised ? VarPrefix.Replace(name, string.Empty) : name)
            .ToArray();
    }

    private static IEnumerable<string> Verify(
        string path,
        string typeName,
        string[] bound,
        Resolution resolution)
    {
        if (resolution.Problem is not null)
        {
            yield return $"{path}: {resolution.Problem}";
            yield break;
        }

        var candidates = resolution.Candidates;
        if (candidates is null)
        {
            yield return $"{path}: record '{typeName}' not found — cannot verify its deconstruction.";
            yield break;
        }

        // Arity picks the overload, exactly as the compiler does. A mismatch here
        // is normally already a compile error; it is reported rather than assumed
        // so the guard degrades to a clear message instead of a wrong one.
        var fields = candidates.FirstOrDefault(c => c.Count == bound.Length);
        if (fields is null)
        {
            yield return $"{path}: '{typeName}' offers no way to deconstruct into {bound.Length} "
                + $"values (arities: {string.Join(", ", candidates.Select(c => c.Count))}). "
                + "A field was added or removed; every binding after it has shifted.";
            yield break;
        }

        for (var i = 0; i < bound.Length; i++)
        {
            var local = bound[i];
            if (local == "_" || Same(local, fields[i]))
            {
                continue;
            }

            var elsewhere = IndexOfField(fields, local);
            if (elsewhere >= 0)
            {
                yield return $"{path}: '{typeName}' position {i} is '{fields[i]}' but is bound as "
                    + $"'{local}', which names position {elsewhere} ('{fields[elsewhere]}'). "
                    + "That is a transposition — the compiler cannot catch it when the types match.";
            }
        }
    }

    private static int IndexOfField(IReadOnlyList<string> fields, string local)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (Same(local, fields[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Same(string local, string field) =>
        string.Equals(local, field, StringComparison.OrdinalIgnoreCase);

    private static string ContextOf(string path)
    {
        var segments = path.Split('/');
        return segments.Length > 2 ? segments[1] : string.Empty;
    }

    /// <summary>
    /// Every way <paramref name="typeName"/> can be deconstructed: the record's
    /// positional header, plus any hand-written <c>Deconstruct</c> overload.
    /// C# selects between them by arity, so the guard must too — checking only
    /// the header would misreport a type that declares its own overload.
    /// The handler's own context wins; otherwise the declaration must be unique.
    /// Known heuristic: records are matched by name with <c>record\s+T\s*\(</c>,
    /// which can also match comments or XML docs — that surfaces as an ambiguity
    /// (loud), never silently. Own-context-first would pick the wrong record if a
    /// handler parameter is a Shared.Contracts record while its own context
    /// declares a same-named record in another namespace; no such collision exists
    /// today, and resolving by namespace was rejected (plan section 5).
    /// </summary>
    private static Resolution Resolve(
        string handlerPath,
        string typeName,
        IReadOnlyDictionary<string, string> sources,
        Dictionary<(string Context, string Name), Resolution> cache)
    {
        var context = ContextOf(handlerPath);
        if (cache.TryGetValue((context, typeName), out var cached))
        {
            return cached;
        }

        Regex declaration = new(@"record\s+" + Regex.Escape(typeName) + @"\s*\(");
        var declared = sources
            .Where(source => declaration.IsMatch(source.Value))
            .Select(source => (Path: source.Key, Context: ContextOf(source.Key), Text: source.Value))
            .ToList();

        var own = declared.Where(d => d.Context == context).ToList();
        var pool = own.Count > 0 ? own : declared;
        var resolution = pool.Count switch
        {
            0 => new Resolution(null, null),
            1 => new Resolution(CandidatesIn(pool[0].Text, declaration), null),
            _ => new Resolution(
                null,
                $"record '{typeName}' is declared {(own.Count > 0 ? "more than once" : "in several contexts")} "
                + $"({string.Join(", ", pool.Select(d => d.Path))}) — cannot tell which this handler deconstructs."),
        };

        cache[(context, typeName)] = resolution;
        return resolution;
    }

    private static List<IReadOnlyList<string>>? CandidatesIn(string text, Regex declaration)
    {
        var match = declaration.Match(text);
        var header = Balanced(text, match.Index + match.Length - 1, '(', ')');
        if (header is null)
        {
            return null;
        }

        List<IReadOnlyList<string>> candidates = [Names(header)];
        candidates.AddRange(DeconstructOverloads(text, match.Index));
        return candidates;
    }

    private static IEnumerable<IReadOnlyList<string>> DeconstructOverloads(string text, int typeIndex)
    {
        foreach (Match overload in Regex.Matches(text[typeIndex..], @"void\s+Deconstruct\s*\("))
        {
            var parameters = Balanced(text, typeIndex + overload.Index + overload.Length - 1, '(', ')');
            if (parameters is not null)
            {
                yield return Names(parameters);
            }
        }
    }

    /// <summary>Parameter names, dropping type, modifiers and any default value.</summary>
    private static List<string> Names(string parameters) =>
        SplitTopLevel(parameters)
            .Select(p => p.Split('=')[0].Trim())
            .Where(p => p.Length > 0)
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1])
            .ToList();

    /// <summary>Splits a parameter list on commas that are not inside generics.</summary>
    private static IEnumerable<string> SplitTopLevel(string parameters)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = parameters[i];
            if (c is '<' or '(' or '[')
            {
                depth++;
            }
            else if (c is '>' or ')' or ']')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return parameters[start..i];
                start = i + 1;
            }
        }

        yield return parameters[start..];
    }

    private static string? BodyAfter(string text, int signatureIndex)
    {
        var open = text.IndexOf('{', signatureIndex);
        return open < 0 ? null : Balanced(text, open, '{', '}');
    }

    /// <summary>Contents between <paramref name="openIndex"/> and its match.</summary>
    private static string? Balanced(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return text[(openIndex + 1)..i];
                }
            }
        }

        return null;
    }

    internal static Dictionary<string, string> ReadSources()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        var root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        var src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(root.FullName, f).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
    }
}
