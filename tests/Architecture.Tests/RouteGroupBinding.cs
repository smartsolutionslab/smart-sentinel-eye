using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// One <c>MapGroup</c> declaration as a guard discovered it: the group local's
/// name, the index its enclosing block is found from, and the span of the
/// declaration itself. Discovery stays each guard's own; the binding below is
/// shared.
/// </summary>
internal sealed record GroupDeclarationSite(string Name, int BlockAnchor, int DeclarationFrom, int DeclarationTo);

/// <summary>
/// What a mapping's receiver reaches at one index.
/// </summary>
internal abstract record GroupBindingOutcome
{
    /// <summary>The receiver reaches the group declared under this name.</summary>
    internal sealed record Bound(string GroupName) : GroupBindingOutcome;

    /// <summary>The receiver reaches no group.</summary>
    internal sealed record NotBound : GroupBindingOutcome;

    /// <summary>
    /// The receiver is a helper's parameter and no single group can be said to
    /// reach it; <see cref="Reason"/> completes the sentence a guard words.
    /// </summary>
    internal sealed record HelperUnresolved(string Method, string Parameter, string Reason) : GroupBindingOutcome;
}

/// <summary>
/// The call-site and block-scope binding of a mapping's receiver to a route
/// group, which <c>EndpointScopeDeclarationTests</c> and
/// <c>StatusProducerDeclarationTests</c> each carried a copy of. It answers
/// with a group <i>name</i>; each guard looks the name up in its own dictionary
/// and words a failed helper in its own way.
/// </summary>
internal static class RouteGroupBinding
{
    /// <summary>
    /// A local or parameter whose declared type makes it a thing routes can be
    /// mapped on.
    /// </summary>
    internal static readonly Regex RouteBuilderDeclaration = new(
        @"\b(?:IEndpointRouteBuilder|WebApplication|RouteGroupBuilder)\s+(?<name>\w+)\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// An in-file helper <c>MapX(RouteGroupBuilder group)</c> with a block or
    /// expression body; its parameter takes the group its callers pass.
    /// </summary>
    private static readonly Regex GroupHelperDeclaration = new(
        @"\b(?<method>\w+)\s*\(\s*RouteGroupBuilder\s+(?<parameter>\w+)\s*\)\s*(?<body>\{|=>)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A single <c>=</c> after a declared name, which marks a local initialiser
    /// rather than a parameter; <c>(?!=|&gt;)</c> excludes <c>==</c> and the
    /// <c>=&gt;</c> of a lambda or expression body.
    /// </summary>
    private static readonly Regex EqualsAssignment = new(
        @"^\s*=(?!=|>)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Reads every route-builder parameter and group local in
    /// <paramref name="masked"/>. A group name only binds when
    /// <paramref name="groupNames"/> holds it, so a caller's dictionary stays
    /// the authority on which names are groups.
    /// </summary>
    internal static GroupBindings Read(
        string masked,
        IReadOnlySet<string> groupNames,
        IEnumerable<GroupDeclarationSite> declarations)
    {
        List<GroupScope> scopes = [];
        foreach (var declaration in declarations)
        {
            var (blockFrom, blockTo) = EnclosingBlock(masked, declaration.BlockAnchor);
            scopes.Add(new GroupScope(
                declaration.Name,
                blockFrom,
                blockTo,
                declaration.DeclarationFrom,
                declaration.DeclarationTo));
        }

        return new GroupBindings(masked, groupNames, ParameterScopes(masked, groupNames), scopes);
    }

    /// <summary>
    /// Every route-builder parameter in the file with the body it is visible in.
    /// A parameter is never looked up by its name in the group dictionary: a
    /// recognised helper takes the group its callers pass, and any other
    /// parameter is bound to nothing and so reads as unreadable.
    /// </summary>
    private static List<ParameterScope> ParameterScopes(string masked, IReadOnlySet<string> groupNames)
    {
        List<ParameterScope> scopes = [];
        HashSet<(string Name, int From)> helperParameters = [];

        foreach (Match helper in GroupHelperDeclaration.Matches(masked))
        {
            var parameter = helper.Groups["parameter"].Value;
            var bodyStart = helper.Groups["body"].Index;
            var (from, to) = BodyAfter(masked, bodyStart);
            var bound = ResolveHelperGroup(masked, groupNames, helper, to);
            helperParameters.Add((parameter, from));
            scopes.Add(new ParameterScope(parameter, from, to, bound));
        }

        foreach (Match declaration in RouteBuilderDeclaration.Matches(masked))
        {
            var name = declaration.Groups["name"].Value;
            var afterName = declaration.Index + declaration.Length;
            if (EqualsAssignment.IsMatch(masked[afterName..]))
            {
                continue;
            }

            var close = ParameterListEnd(masked, afterName);
            if (close < 0)
            {
                continue;
            }

            var (from, to) = BodyAfter(masked, close + 1);
            if (to > from && !helperParameters.Contains((name, from)))
            {
                scopes.Add(new ParameterScope(name, from, to, new GroupBindingOutcome.NotBound()));
            }
        }

        return scopes;
    }

    /// <summary>
    /// The block a declaration is visible in: the innermost <c>{ }</c> around
    /// it, or the whole file when it sits in top-level statements.
    /// </summary>
    private static (int From, int To) EnclosingBlock(string masked, int index)
    {
        var depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            switch (masked[i])
            {
                case '}':
                    depth++;
                    break;
                case '{' when depth == 0:
                    return (i, RouteChainReader.Balanced(masked, i, '{', '}'));
                case '{':
                    depth--;
                    break;
            }
        }

        return (0, masked.Length);
    }

    private static int ParameterListEnd(string masked, int from)
    {
        var depth = 0;
        for (var i = from; i < masked.Length; i++)
        {
            switch (masked[i])
            {
                case '(':
                    depth++;
                    break;
                case ')' when depth == 0:
                    return i;
                case ')':
                    depth--;
                    break;
                case ';' or '{' or '}':
                    return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// The span of the block or expression body that starts at
    /// <paramref name="from"/> (after whitespace), or an empty span when what
    /// follows is not a body.
    /// </summary>
    private static (int From, int To) BodyAfter(string masked, int from)
    {
        var start = from;
        while (start < masked.Length && char.IsWhiteSpace(masked[start]))
        {
            start++;
        }

        if (start < masked.Length && masked[start] == '{')
        {
            return (start, RouteChainReader.Balanced(masked, start, '{', '}'));
        }

        if (start + 1 < masked.Length && masked[start] == '=' && masked[start + 1] == '>')
        {
            return (start, RouteChainReader.StatementEnd(masked, start, ChainEndSentinel.EndOfText, ChainLiteralHandling.AlreadyMasked));
        }

        return (start, start);
    }

    /// <summary>
    /// The group a helper's parameter receives: the one group every call site in
    /// the file passes as a bare identifier. Anything else is unresolved, so the
    /// helper's mappings fail rather than inherit a guess.
    /// </summary>
    private static GroupBindingOutcome ResolveHelperGroup(
        string masked,
        IReadOnlySet<string> groupNames,
        Match helper,
        int bodyEnd)
    {
        var method = helper.Groups["method"].Value;
        GroupBindingOutcome Unresolved(string reason) => new GroupBindingOutcome.HelperUnresolved(
            method,
            helper.Groups["parameter"].Value,
            reason);

        HashSet<string> passed = new(StringComparer.Ordinal);
        var references = Regex.Matches(masked, $@"\b{Regex.Escape(method)}\b", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Where(reference => reference.Index < helper.Index || reference.Index >= bodyEnd);

        foreach (var reference in references)
        {
            var afterName = reference.Index + reference.Length;
            var open = afterName;
            while (open < masked.Length && char.IsWhiteSpace(masked[open]))
            {
                open++;
            }

            if (open >= masked.Length || masked[open] != '(')
            {
                return Unresolved($"'{method}' is referenced without being called, so the group it receives cannot be established");
            }

            var close = RouteChainReader.Balanced(masked, open, '(', ')');
            var arguments = RouteChainReader.SplitArguments(masked, open + 1, close);
            var argument = arguments.Count == 1 ? masked[arguments[0].Start..arguments[0].End].Trim() : string.Empty;
            if (!groupNames.Contains(argument))
            {
                return Unresolved($"is called with '{masked[(open + 1)..close].Trim()}', which is not a single route group declared in this file");
            }

            passed.Add(argument);
        }

        return passed.Count switch
        {
            0 => Unresolved("is never called in this file, so the group it receives cannot be established"),
            1 => new GroupBindingOutcome.Bound(passed.Single()),
            _ => Unresolved($"is called with more than one group ({string.Join(", ", passed.Order(StringComparer.Ordinal))}), so which group's authorization it inherits cannot be established"),
        };
    }

    internal sealed record ParameterScope(string Name, int From, int To, GroupBindingOutcome Outcome);

    /// <summary>
    /// Where a group local is visible: the block that declares it, and the span
    /// of the declaration itself.
    /// </summary>
    internal sealed record GroupScope(string Name, int From, int To, int DeclarationFrom, int DeclarationTo);

    /// <summary>
    /// The bindings of one file, ready to answer for a receiver at an index.
    /// </summary>
    internal sealed class GroupBindings
    {
        private readonly string masked;
        private readonly IReadOnlySet<string> groupNames;
        private readonly List<ParameterScope> parameters;
        private readonly List<GroupScope> scopes;

        internal GroupBindings(
            string masked,
            IReadOnlySet<string> groupNames,
            List<ParameterScope> parameters,
            List<GroupScope> scopes)
        {
            this.masked = masked;
            this.groupNames = groupNames;
            this.parameters = parameters;
            this.scopes = scopes;
        }

        internal GroupBindingOutcome Resolve(string receiver, int index)
        {
            var parameter = parameters
                .Where(scope => scope.Name == receiver && scope.From <= index && index < scope.To)
                .OrderBy(scope => scope.To - scope.From)
                .FirstOrDefault();

            if (parameter is not null)
            {
                return parameter.Outcome;
            }

            var visible = scopes.Any(scope => scope.Name == receiver
                && scope.From <= index
                && index < scope.To
                && !IsRedeclared(scope, index));

            return visible && groupNames.Contains(receiver)
                ? new GroupBindingOutcome.Bound(receiver)
                : new GroupBindingOutcome.NotBound();
        }

        /// <summary>
        /// Whether the group's name is declared again — as an out variable, a
        /// lambda parameter or a typed local or parameter — between the group's
        /// declaration and <paramref name="index"/>, so the name no longer means it.
        /// </summary>
        private bool IsRedeclared(GroupScope scope, int index)
        {
            var name = Regex.Escape(scope.Name);
            var redeclaration = new Regex(
                $@"\bout\s+(?:[\w.<>?\[\]]+\s+)?{name}\b"
                    + $@"|\b{name}\s*=>"
                    + $@"|\b(?!return\b|await\b|in\b|new\b|is\b|not\b|and\b|or\b|else\b|case\b|throw\b|yield\b)[\w.<>?\[\]]+\s+{name}\s*(?:[=;,)]|\bin\b)",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            return redeclaration.Matches(masked, scope.From)
                .Any(match => match.Index < index
                    && (match.Index < scope.DeclarationFrom || match.Index >= scope.DeclarationTo));
        }
    }
}
