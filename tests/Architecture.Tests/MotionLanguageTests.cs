using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards spec 292 (issue #2334), ADR-0146's motion stance and ADR-0148's naming: the
/// wall gets none of the console's motion except the one named signal, every
/// paint-property transition is deliberate and named, every transition cites a role
/// (never a scale step), every keyframe animates compositor properties only, reduced
/// motion redesigns rather than deletes, allowlists stay honest, and the state role
/// stays bound to Tailwind's transition defaults.
///
/// <para>
/// Follows <see cref="SharedUiTokenUsageTests"/>'s shape: a comment-stripped scan,
/// rules applied to string-literal content for Tailwind-utility-shaped text, shrink-only
/// allowlists with an honesty fact, failure messages that say what to do instead. The CSS
/// side reuses no existing reader — <see cref="DesignTokenLayerTests"/>'s is a flat
/// <c>:root</c>/theme-block parser with no nesting, and this guard needs the nested
/// <c>@layer</c> / <c>@media</c> / <c>@keyframes</c> shapes both <c>index.css</c> files
/// use — so <see cref="ParseCssRules"/> is a small local brace-depth walker instead.
/// </para>
///
/// <para>
/// <b>The kiosk reach set</b> (fact 1) is not "the kiosk's own files": it is every
/// non-test file under <c>apps/kiosk-web/src</c> plus the transitive closure of
/// <c>apps/shared/src</c> modules reached from it through import specifiers, resolved
/// the same way a bundler would — relative specifiers against the importing file, and
/// <c>@smart-sentinel-eye/shared/…</c> specifiers through <c>apps/shared/package.json</c>'s
/// <c>exports</c> map (exact and wildcard keys). <see cref="The_kiosk_reach_set_includes_a_known_shared_member"/>
/// pins that the closure actually reaches <c>CameraViewer.tsx</c> — a resolver regression
/// that silently shrank the scope would otherwise pass every other fact vacuously.
/// </para>
///
/// <para>
/// <b>Measured on develop (spec 292 plan.md §4, tasks.md T003):</b> facts 1 and 2 are red
/// on exactly <c>PickerPage.tsx</c> (two bare <c>transition</c> sites); fact 3 is red on
/// exactly <c>Button.tsx</c> (its <c>transition-colors</c> names no role); fact 7 is red
/// because no <c>--duration-&lt;role&gt;</c>/<c>--ease-&lt;role&gt;</c> token exists yet.
/// Facts 4, 5 and 6 are green on develop — there is nothing yet that could violate them —
/// and are proved by a planted-violation counterfactual instead (spec 292 phase-4a report),
/// not by a red here.
/// </para>
/// </summary>
public class MotionLanguageTests
{
    private const string KioskSrc = "apps/kiosk-web/src";
    private const string ManagementSrc = "apps/management-web/src";

    // Scoped to apps/shared/src/ui (not the whole apps/shared/src tree), mirroring
    // SharedUiTokenUsageTests' own convention: facts 2/3/5(b) scan for Tailwind-utility-
    // shaped string-literal content, and apps/shared/src/api|realtime|observability|
    // streaming carry no JSX/className — including them only risks a false positive on
    // prose (e.g. a `transition: 'session-release-failed'` log field) for no coverage gain.
    private const string SharedUiSrc = "apps/shared/src/ui";

    private const string TokensCssPath = "apps/shared/src/ui/tokens/tokens.css";
    private const string KioskIndexCssPath = "apps/kiosk-web/src/styles/index.css";
    private const string SharedPackageJsonPath = "apps/shared/package.json";

    /// <summary>Fact 6's honesty check: does the allowlisted file still say why (substring, comment-stripped).</summary>
    private const string PulseChainMarker = "ssE-overlay-highlight";

    /// <summary>Facts 1/4/5's exemption test: the pulse's own rule, matched exactly — never a substring
    /// (a <c>.ssE-overlay-highlight-banner</c> selector or a differently-suffixed keyframe must not ride
    /// along).</summary>
    private const string PulseSelector = ".ssE-overlay-highlight";

    /// <summary>Facts 1/4/5's exemption test for the keyframe itself, matched exactly.</summary>
    private const string PulseKeyframeName = "ssE-overlay-highlight-pulse";

    // =====================================================================
    // Shrink-only allowlists (fact 6 keeps them honest).
    // =====================================================================

    /// <summary>
    /// Fact 1's one wall exception: the highlight pulse is motion AS the signal
    /// (ADR-0146), matched by its keyframe/class name appearing anywhere in a CSS
    /// rule's at-rule/selector chain (covers the base rule, the <c>@keyframes</c>
    /// itself, and the reduced-motion redefinition, all three of which name it).
    /// </summary>
    private static readonly (string RelativePath, string Match, string Reason)[] WallMotionAllowlist =
    [
        (
            KioskIndexCssPath,
            PulseChainMarker,
            "ADR-0146: motion as the alert — the one wall animation that is itself a signal."),
    ];

    /// <summary>Fact 2's one paint-transition exception.</summary>
    private static readonly (string RelativePath, string Match, string Reason)[] PaintTransitionAllowlist =
    [
        (
            "apps/shared/src/ui/primitives/Button.tsx",
            "transition-colors",
            "spec 292 §4: a colour cross-fade on one control on interaction; never rendered on "
            + "the wall (fact 1)."),
    ];

    /// <summary>Fact 5's one "deletes rather than redesigns" exception.</summary>
    private static readonly (string RelativePath, string Match, string Reason)[] ReducedMotionDeletionAllowlist =
    [
        (
            KioskIndexCssPath,
            "animation: none",
            "ADR-0146: a looping signal; the static ring stays and still marks the tile."),
    ];

    // =====================================================================
    // Regexes.
    // =====================================================================

    /// <summary>
    /// Lookaround-bounded, not <c>\b</c>-bounded: a token ending in <c>]</c> or <c>)</c> (the
    /// bracket/paren arbitrary-value forms) is followed by a quote in real source, and <c>\b</c>
    /// does not fire between two non-word characters — a <c>\b</c>-anchored version of this regex
    /// silently truncates <c>transition-(--my-props)</c> down to bare <c>transition</c> (still a
    /// match, by accident of the optional group, but not a reliable one for every alternative).
    /// Tailwind v4's parenthesised arbitrary-value form (<c>duration-(--duration-fast)</c>) sits
    /// alongside the bracket form throughout.
    /// </summary>
    private static readonly Regex MotionUtilityToken = new(
        @"(?<![\w-])(?:transition(?:-[a-z]+|-\[[^\]]*\]|-\([^)]*\))?|animate-[\w-]+"
        + @"|duration-(?:[\w-]+|\[[^\]]*\]|\([^)]*\))|ease-(?:[\w-]+|\[[^\]]*\]|\([^)]*\))"
        + @"|delay-(?:[\w-]+|\[[^\]]*\]|\([^)]*\)))(?![\w-])",
        RegexOptions.Compiled);

    private static readonly Regex MotionVariant = new(@"motion-(?:safe|reduce):", RegexOptions.Compiled);

    /// <summary>
    /// A string-valued <c>transition</c>/<c>animation*</c> key, scoped to a JSX
    /// <c>style={{ ... }}</c> body by the caller — never applied to whole-file raw
    /// source. Applying it unscoped is a proven matcher defect: <c>CameraViewer.tsx</c>
    /// has a callback parameter literally named <c>transition</c> typed as a string
    /// union (<c>transition: 'decode-sampler-failed' | 'lag-sampler-failed'</c>), which
    /// this pattern matches on an unscoped scan even though it is a type annotation, not
    /// a CSS-in-JS style object.
    /// </summary>
    private static readonly Regex InlineStyleMotionKey = new(
        @"\b(?:transition|animation)[A-Za-z]*\s*:\s*['""`]", RegexOptions.Compiled);

    private static readonly Regex JsxStyleObjectBody = new(@"style=\{\{(?<body>[\s\S]*?)\}\}", RegexOptions.Compiled);

    private static readonly Regex WebAnimationsCall = new(@"\.animate\(", RegexOptions.Compiled);

    /// <summary>
    /// A real CSS <c>animation</c>/<c>transition</c> property declaration — never a
    /// custom property whose *name* merely contains the word (e.g.
    /// <c>--default-transition-duration</c>), excluded by the lookbehind.
    /// </summary>
    private static readonly Regex CssMotionDeclaration = new(
        @"(?<![-\w])(?:animation|transition)(?:-[a-z]+)?\s*:", RegexOptions.Compiled);

    private static readonly Regex CssZeroDuration = new(
        @"(?<![-\w])(?:animation|transition)-duration\s*:\s*0(?:s|ms)?\b", RegexOptions.Compiled);

    private static readonly Regex CssNoneValue = new(
        @"(?<![-\w])(?:animation(?:-name)?|transition)\s*:\s*none\b", RegexOptions.Compiled);

    private static readonly Regex CssCustomDurationRedeclare = new(@"^--duration-", RegexOptions.Compiled);

    private static readonly Regex BannedTransitionUtility = new(
        @"(?<![\w-])transition(?:-all|-shadow|-\[[^\]]*\]|-\([^)]*\))?(?![\w-])", RegexOptions.Compiled);

    private static readonly Regex TransitionColorsUtility = new(
        @"(?<![\w-])transition-colors(?![\w-])", RegexOptions.Compiled);

    /// <summary>
    /// Fact 3's "given a transition-* utility" — deliberately excludes the BARE
    /// <c>transition</c> shorthand. A bare <c>transition</c> resolves through
    /// Tailwind's default-transition bridge, not an explicit duration citation, and
    /// facts 1/2 already ban it outright regardless of any role. Measured: an
    /// unscoped match also caught PickerPage.tsx's bare <c>transition</c>, which
    /// facts 1/2 already cover, so this one is scoped to the suffixed forms only.
    /// </summary>
    private static readonly Regex SuffixedTransitionUtility = new(
        @"(?<![\w-])transition-(?:[a-z]+|\[[^\]]*\]|\([^)]*\))(?![\w-])", RegexOptions.Compiled);

    private static readonly Regex RoleDurationToken = new(
        @"(?<![\w-])duration-(?:state|enter|exit|route)(?![\w-])", RegexOptions.Compiled);

    /// <summary>Fact 3's other half of "names its role": the matching <c>ease-&lt;role&gt;</c>, so
    /// a transition that names a duration role but leaves easing to the browser default still
    /// fails — the duration role alone was silently sufficient before this addition.</summary>
    private static readonly Regex RoleEaseToken = new(
        @"(?<![\w-])ease-(?:state|enter|exit|route)(?![\w-])", RegexOptions.Compiled);

    private static readonly Regex ScaleDurationOrEase = new(
        @"(?<![\w-])(?:duration-(?:fast|moderate|slow|\d+|\[[^\]]*\]|\([^)]*\))"
        + @"|ease-(?:out|in|in-out|linear|\[[^\]]*\]|\([^)]*\)))(?![\w-])",
        RegexOptions.Compiled);

    /// <summary>
    /// Static <c>import ... from '...'</c> / bare <c>import '...'</c> forms, plus dynamic
    /// <c>import('...')</c> calls — the second alternative, since the first requires whitespace
    /// immediately before the quote and a dynamic call has <c>(</c> there instead.
    /// </summary>
    private static readonly Regex JsImportSpecifier = new(
        @"(?:from|import)\s+['""](?<spec>[^'""]+)['""]|import\s*\(\s*['""](?<spec>[^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly Regex CssImportSpecifier = new(
        @"@import\s+['""](?<spec>[^'""]+)['""]", RegexOptions.Compiled);

    private static readonly Regex MotionReduceNoneUtility = new(
        @"motion-reduce:(?:animate|transition)-none", RegexOptions.Compiled);

    private static readonly string[] CompositorProperties = ["opacity", "transform", "translate", "scale", "rotate"];

    /// <summary>Fact 5(a)'s "needs an opacity-only reduce redefinition" trigger — every property
    /// fact 4 permits as a compositor property except <c>opacity</c> itself moves the element, so
    /// all four (not <c>transform</c> alone) require the redesign.</summary>
    private static readonly string[] TravelProperties = ["transform", "translate", "scale", "rotate"];

    // =====================================================================
    // Fact 1 — the wall has no motion but its signal.
    // =====================================================================

    /// <summary>
    /// Red on develop: <c>PickerPage.tsx</c>'s two bare <c>transition</c> sites.
    /// </summary>
    [Fact]
    public void The_wall_has_no_motion_but_its_signal()
    {
        DirectoryInfo root = RepositorySource.Root();
        HashSet<string> reachSet = KioskReachClosure(root);

        List<string> violations = [];

        foreach (string relativePath in reachSet)
        {
            string fullPath = Path.Combine(root.FullName, relativePath);
            string raw = File.ReadAllText(fullPath);

            if (relativePath.EndsWith(".css", StringComparison.Ordinal))
            {
                violations.AddRange(CssWallViolations(relativePath, StripComments(raw, CommentStyle.CssOnly)));
                continue;
            }

            string stripped = StripComments(raw, CommentStyle.TsAndBlock);
            string literalContent = TypeScriptSource.StringLiteralContent(raw);

            foreach (Match match in MotionUtilityToken.Matches(literalContent))
            {
                violations.Add($"{relativePath}: Tailwind motion utility '{match.Value}' in a class string");
            }

            foreach (Match match in MotionVariant.Matches(literalContent))
            {
                violations.Add($"{relativePath}: motion-safe:/motion-reduce: variant '{match.Value}'");
            }

            foreach (Match styleBody in JsxStyleObjectBody.Matches(stripped))
            {
                foreach (Match match in InlineStyleMotionKey.Matches(styleBody.Groups["body"].Value))
                {
                    violations.Add($"{relativePath}: inline style key '{match.Value.TrimEnd('\'', '"', '`', ':', ' ')}'");
                }
            }

            if (WebAnimationsCall.IsMatch(stripped))
            {
                violations.Add($"{relativePath}: a Web Animations .animate( call");
            }
        }

        // Prohibition by absence (plan.md §4 fact 1): the kiosk's own index.css must
        // never import motion.css, regardless of what the closure above finds.
        string kioskIndexCss = StripComments(
            File.ReadAllText(Path.Combine(root.FullName, KioskIndexCssPath)), CommentStyle.CssOnly);
        if (kioskIndexCss.Contains("motion.css", StringComparison.Ordinal)
            || kioskIndexCss.Contains("ui/motion", StringComparison.Ordinal))
        {
            violations.Add($"{KioskIndexCssPath}: imports motion.css — the kiosk must never reach for it (ADR-0146)");
        }

        violations.ShouldBeEmpty(
            "the wall (or a shared module it reaches) has motion other than the named signal pulse: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}"))
            + Environment.NewLine
            + "ADR-0146: the wall subtracts every animation that is not itself a signal.");
    }

    /// <summary>
    /// R4 (plan.md §4): a resolver regression that silently shrank the kiosk reach
    /// closure would make fact 1 pass vacuously. Pinning a known member fails loudly
    /// instead.
    /// </summary>
    [Fact]
    public void The_kiosk_reach_set_includes_a_known_shared_member()
    {
        DirectoryInfo root = RepositorySource.Root();
        HashSet<string> reachSet = KioskReachClosure(root);

        reachSet.ShouldContain(
            "apps/shared/src/ui/composites/CameraViewer.tsx",
            "the kiosk reach closure does not contain CameraViewer.tsx — the import resolver "
            + "(relative specifiers, and @smart-sentinel-eye/shared/... through package.json's "
            + "exports map) has regressed and silently shrunk fact 1's scope.");
    }

    private static List<string> CssWallViolations(string relativePath, string css)
    {
        List<string> violations = [];

        foreach (CssRule rule in ParseCssRules(css))
        {
            if (IsAllowlistedPulseRule(WallMotionAllowlist, relativePath, rule))
            {
                continue;
            }

            if (rule.AtRuleChain.Concat([rule.Selector]).Any(
                    part => part.TrimStart().StartsWith("@keyframes", StringComparison.Ordinal)))
            {
                violations.Add($"{relativePath}: @keyframes ({string.Join(" > ", rule.AtRuleChain)} > {rule.Selector})");
                continue;
            }

            foreach ((string prop, string value) in rule.Declarations)
            {
                if (CssMotionDeclaration.IsMatch($"{prop}:"))
                {
                    violations.Add($"{relativePath}: '{prop}: {value};' in {rule.Selector}");
                }
            }
        }

        return violations;
    }

    // =====================================================================
    // Fact 2 — a paint transition is deliberate.
    // =====================================================================

    /// <summary>Red on develop: <c>PickerPage.tsx</c>'s two bare <c>transition</c> sites.</summary>
    [Fact]
    public void A_paint_transition_is_deliberate()
    {
        DirectoryInfo root = RepositorySource.Root();
        HashSet<string> paintAllowlistPaths = [.. PaintTransitionAllowlist.Select(entry => entry.RelativePath)];

        List<string> violations = [];

        foreach (string relativePath in ScannedTsFiles(root))
        {
            string literalContent = TypeScriptSource.StringLiteralContent(
                File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in BannedTransitionUtility.Matches(literalContent))
            {
                violations.Add($"{relativePath}: '{match.Value}' — animates paint properties by default");
            }

            if (!paintAllowlistPaths.Contains(relativePath))
            {
                foreach (Match match in TransitionColorsUtility.Matches(literalContent))
                {
                    violations.Add(
                        $"{relativePath}: '{match.Value}' outside the paint-transition allowlist");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a paint-property transition is not deliberate (not on the allowlist), or is one of the "
            + "always-banned bare/-all/-shadow/-[...] forms: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}"))
            + Environment.NewLine
            + "cite the semantic role instead, or add a reasoned, issue-tagged allowlist entry.");
    }

    // =====================================================================
    // Fact 3 — every transition names its role.
    // =====================================================================

    /// <summary>Red on develop: <c>Button.tsx</c>'s <c>transition-colors</c> names no role.</summary>
    [Fact]
    public void Every_transition_names_its_role()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedTsFiles(root))
        {
            string literalContent = TypeScriptSource.StringLiteralContent(
                File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (string literal in literalContent.Split('\n'))
            {
                if (SuffixedTransitionUtility.IsMatch(literal)
                    && (!RoleDurationToken.IsMatch(literal) || !RoleEaseToken.IsMatch(literal)))
                {
                    violations.Add(
                        $"{relativePath}: '{literal.Trim()}' has a transition utility but names no "
                        + "duration role, no ease role, or neither");
                }

                foreach (Match match in ScaleDurationOrEase.Matches(literal))
                {
                    violations.Add($"{relativePath}: '{match.Value}' cites the scale directly, not a role");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a transition call site does not name one of the four roles (state, enter, exit, route), "
            + "or cites a scale step / Tailwind's stock ease directly: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    // =====================================================================
    // Fact 4 — every keyframe composites.
    // =====================================================================

    /// <summary>
    /// Green on develop (declared in advance, spec §7) — nothing yet violates it; proved
    /// by a planted-violation counterfactual instead (spec 292 phase-4a report).
    /// </summary>
    [Fact]
    public void Every_keyframe_composites()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedCssFiles(root))
        {
            string css = StripComments(File.ReadAllText(Path.Combine(root.FullName, relativePath)), CommentStyle.CssOnly);

            foreach (CssRule rule in ParseCssRules(css))
            {
                if (!rule.AtRuleChain.Any(part => part.TrimStart().StartsWith("@keyframes", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (IsAllowlistedPulseRule(WallMotionAllowlist, relativePath, rule))
                {
                    continue;
                }

                foreach ((string prop, string value) in rule.Declarations)
                {
                    if (!CompositorProperties.Contains(prop, StringComparer.Ordinal))
                    {
                        violations.Add($"{relativePath}: keyframe frame '{rule.Selector}' declares '{prop}: {value};'");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "a @keyframes frame animates a non-compositor property (only opacity, transform, "
            + "translate, scale and rotate are allowed, except the allowlisted pulse): "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    // =====================================================================
    // Fact 5 — reduced motion redesigns rather than deletes.
    // =====================================================================

    /// <summary>
    /// Green on develop (declared in advance, spec §7) — nothing yet violates it; proved
    /// by planted-violation counterfactuals instead (spec 292 phase-4a report).
    /// </summary>
    [Fact]
    public void Reduced_motion_redesigns_rather_than_deletes()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        // (a) + (c): CSS reduce-media deletions, and travel-property keyframes missing an
        // opacity-only reduce redefinition.
        var travelKeyframes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Per (relativePath, keyframeName): true only while every reduce-block frame seen so far
        // for that keyframe is opacity-only. A partial redefinition (one opacity-only frame, one
        // that still travels) must read as NOT redesigned, so this ANDs across frames rather than
        // ORing — the previous HashSet-of-"any opacity-only frame seen" let one honest frame
        // launder a dishonest sibling.
        var reduceKeyframeAllFramesOpacityOnly = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (string relativePath in ScannedCssFiles(root))
        {
            string css = StripComments(File.ReadAllText(Path.Combine(root.FullName, relativePath)), CommentStyle.CssOnly);
            List<CssRule> rules = ParseCssRules(css);

            foreach (CssRule rule in rules)
            {
                bool insideReduceMedia = rule.AtRuleChain.Any(IsReducedMotionMediaPrelude);
                string? keyframeName = KeyframeNameFromChain(rule.AtRuleChain);

                if (keyframeName is not null)
                {
                    string key = $"{relativePath}::{keyframeName}";

                    if (insideReduceMedia)
                    {
                        bool frameOpacityOnly = rule.Declarations.All(d => d.Prop == "opacity");
                        reduceKeyframeAllFramesOpacityOnly[key] = reduceKeyframeAllFramesOpacityOnly.TryGetValue(
                            key, out bool allSoFar)
                            ? allSoFar && frameOpacityOnly
                            : frameOpacityOnly;
                    }
                    else if (rule.Declarations.Any(d => TravelProperties.Contains(d.Prop, StringComparer.Ordinal)))
                    {
                        if (!travelKeyframes.TryGetValue(relativePath, out HashSet<string>? names))
                        {
                            names = [];
                            travelKeyframes[relativePath] = names;
                        }

                        names.Add(keyframeName);
                    }
                }

                if (!insideReduceMedia)
                {
                    continue;
                }

                bool ruleIsAllowlistedPulse = IsAllowlistedPulseRule(ReducedMotionDeletionAllowlist, relativePath, rule);

                foreach ((string prop, string value) in rule.Declarations)
                {
                    string declarationText = $"{prop}: {value};";

                    // Scoped to the exact allowlisted declaration, not the whole rule: a
                    // zero-duration or a second `animation: none` sharing the pulse's rule must
                    // still be caught, and only "animation: none;" itself is the documented
                    // exception (ADR-0146: the looping signal, static ring left in place).
                    bool isExemptDeclaration = ruleIsAllowlistedPulse && declarationText == "animation: none;";

                    if (CssNoneValue.IsMatch(declarationText) && !isExemptDeclaration)
                    {
                        violations.Add($"{relativePath}: '{declarationText}' inside a reduced-motion block deletes motion");
                    }

                    if (CssZeroDuration.IsMatch(declarationText) && !isExemptDeclaration)
                    {
                        violations.Add($"{relativePath}: '{declarationText}' zeroes a duration inside a reduced-motion block");
                    }

                    if (CssCustomDurationRedeclare.IsMatch(prop))
                    {
                        violations.Add($"{relativePath}: '{prop}' redeclared inside a reduced-motion block");
                    }
                }
            }
        }

        foreach ((string relativePath, HashSet<string> names) in travelKeyframes)
        {
            foreach (string name in names)
            {
                string key = $"{relativePath}::{name}";
                bool redesigned = reduceKeyframeAllFramesOpacityOnly.TryGetValue(key, out bool allFramesOpacityOnly)
                    && allFramesOpacityOnly;

                if (!redesigned)
                {
                    violations.Add(
                        $"{relativePath}: @keyframes {name} declares a travel property (transform/translate/"
                        + "scale/rotate) but has no opacity-only redefinition — every frame, not just one — "
                        + "inside a prefers-reduced-motion: reduce block");
                }
            }
        }

        // (b): no motion-reduce:(animate|transition)-none utility anywhere.
        foreach (string relativePath in ScannedTsFiles(root))
        {
            string literalContent = TypeScriptSource.StringLiteralContent(
                File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in MotionReduceNoneUtility.Matches(literalContent))
            {
                violations.Add($"{relativePath}: '{match.Value}' deletes motion under reduced motion");
            }
        }

        violations.ShouldBeEmpty(
            "reduced motion deletes rather than redesigns motion (only the allowlisted pulse may set "
            + "`animation: none`): "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    // =====================================================================
    // Fact 6 — each allowlist entry still applies.
    // =====================================================================

    [Fact]
    public void Each_allowlist_entry_still_applies()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> problems = [];

        (string RelativePath, string Match, string Reason)[] allEntries =
        [
            .. WallMotionAllowlist,
            .. PaintTransitionAllowlist,
            .. ReducedMotionDeletionAllowlist,
        ];

        foreach ((string relativePath, string match, string reason) in allEntries)
        {
            if (reason.Length == 0)
            {
                problems.Add($"{relativePath} ({match}) has no reason recorded.");
            }

            string path = Path.Combine(root.FullName, relativePath);
            if (!File.Exists(path))
            {
                problems.Add($"{relativePath} no longer exists — remove its allowlist entry.");
                continue;
            }

            // Comment-stripped: a comment mentioning the marker (e.g. left behind after the real
            // rule it described was deleted) must not launder this check — the marker has to be
            // live code, not prose.
            CommentStyle style = relativePath.EndsWith(".css", StringComparison.Ordinal)
                ? CommentStyle.CssOnly
                : CommentStyle.TsAndBlock;
            string liveText = StripComments(File.ReadAllText(path), style);

            if (!liveText.Contains(match, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{relativePath} no longer contains '{match}' — its allowlist entry no longer applies and "
                    + "must be removed (the list is shrink-only, never grows to cover something new).");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    // =====================================================================
    // Fact 7 — the transition default is the state role.
    // =====================================================================

    /// <summary>Red on develop: no role token exists yet.</summary>
    [Fact]
    public void The_transition_default_is_the_state_role()
    {
        DirectoryInfo root = RepositorySource.Root();
        string tokensCss = StripComments(
            File.ReadAllText(Path.Combine(root.FullName, TokensCssPath)), CommentStyle.CssOnly);
        Dictionary<string, string> rootMap = RootDeclarations(tokensCss);

        string[] requiredRoles =
        [
            "--duration-state", "--duration-enter", "--duration-exit", "--duration-route",
            "--ease-state", "--ease-enter", "--ease-exit", "--ease-route",
        ];

        string[] missing = [.. requiredRoles.Where(name => !rootMap.ContainsKey(name))];
        missing.ShouldBeEmpty(
            $"{TokensCssPath} declares no {string.Join(", ", missing)} — spec 292's eight motion-role "
            + "tokens do not exist yet.");

        rootMap.TryGetValue("--default-transition-duration", out string? defaultDuration);
        rootMap.TryGetValue("--default-transition-timing-function", out string? defaultEasing);

        defaultDuration.ShouldBe(
            rootMap["--duration-state"],
            "--default-transition-duration and --duration-state must cite the same var(--duration-…) — "
            + "the bridge and the state role must never drift apart (spec 292 plan.md §2).");
        defaultEasing.ShouldBe(
            rootMap["--ease-state"],
            "--default-transition-timing-function and --ease-state must cite the same var(--ease-…).");

        List<string> outOfRange = [];
        string[] roles = ["state", "enter", "exit", "route"];
        foreach (string role in roles)
        {
            string name = $"--duration-{role}";
            if (!rootMap.ContainsKey(name))
            {
                continue;
            }

            int milliseconds = ResolveDurationMs(name, rootMap, []);
            if (milliseconds < 120 || milliseconds > 200)
            {
                outOfRange.Add($"{name} resolves to {milliseconds}ms, outside ADR-0146's 120-200ms");
            }
        }

        outOfRange.ShouldBeEmpty(string.Join(Environment.NewLine, outOfRange));
    }

    private static int ResolveDurationMs(string name, Dictionary<string, string> map, HashSet<string> seen)
    {
        bool found = map.TryGetValue(name, out string? value);
        found.ShouldBeTrue($"{name} has no declaration to resolve.");
        string trimmed = value.ShouldNotBeNull().Trim();

        Match varMatch = Regex.Match(trimmed, @"^var\((?<name>--[A-Za-z0-9-]+)\)$");
        if (varMatch.Success)
        {
            seen.Add(name).ShouldBeTrue($"circular var() reference resolving {name}.");
            return ResolveDurationMs(varMatch.Groups["name"].Value, map, seen);
        }

        Match msMatch = Regex.Match(trimmed, @"^(?<n>\d+)ms$");
        msMatch.Success.ShouldBeTrue($"{name} resolves to '{trimmed}', not a var() or a plain 'Nms' literal.");
        return int.Parse(msMatch.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // =====================================================================
    // The kiosk reach closure (fact 1's scope).
    // =====================================================================

    private static HashSet<string> KioskReachClosure(DirectoryInfo root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (string relativePath in ScannedFiles(root, KioskSrc, [".ts", ".tsx", ".css"]))
        {
            visited.Add(relativePath);
            queue.Enqueue(relativePath);
        }

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            string fullPath = Path.Combine(root.FullName, current);
            string text = File.ReadAllText(fullPath);

            foreach (string specifier in ImportSpecifiers(text))
            {
                string? resolved = ResolveImport(root, current, specifier);
                if (resolved is null
                    || !resolved.StartsWith("apps/shared/", StringComparison.Ordinal)
                    || resolved.Contains(".test.", StringComparison.Ordinal)
                    || resolved.Contains(".spec.", StringComparison.Ordinal)
                    || !File.Exists(Path.Combine(root.FullName, resolved))
                    || !visited.Add(resolved))
                {
                    continue;
                }

                queue.Enqueue(resolved);
            }
        }

        return visited;
    }

    private static IEnumerable<string> ImportSpecifiers(string source)
    {
        foreach (Match match in JsImportSpecifier.Matches(source))
        {
            yield return match.Groups["spec"].Value;
        }

        foreach (Match match in CssImportSpecifier.Matches(source))
        {
            yield return match.Groups["spec"].Value;
        }
    }

    private static string? ResolveImport(DirectoryInfo root, string importerRelativePath, string specifier)
    {
        const string SharedPackage = "@smart-sentinel-eye/shared";
        const string SharedPrefix = SharedPackage + "/";

        // The bare package specifier (no trailing `/...`) resolves through package.json's `"."`
        // root export, not a `"./<subpath>"` key — matching on the trailing-slash prefix alone
        // silently skipped it.
        if (specifier == SharedPackage)
        {
            return ResolveSharedExport(root, string.Empty);
        }

        if (specifier.StartsWith(SharedPrefix, StringComparison.Ordinal))
        {
            return ResolveSharedExport(root, specifier[SharedPrefix.Length..]);
        }

        if (!specifier.StartsWith('.'))
        {
            return null;
        }

        string importerDirectory = Path.GetDirectoryName(Path.Combine(root.FullName, importerRelativePath))!;
        string combined = Path.GetFullPath(Path.Combine(importerDirectory, specifier));

        string? resolved = ResolveFileCandidate(combined);
        return resolved is null ? null : RepositorySource.RelativePath(root, resolved);
    }

    private static string? ResolveFileCandidate(string combinedPath)
    {
        if (File.Exists(combinedPath))
        {
            return combinedPath;
        }

        if (combinedPath.EndsWith(".js", StringComparison.Ordinal))
        {
            string withoutJs = combinedPath[..^3];
            string[] extensions = [".ts", ".tsx"];
            foreach (string extension in extensions)
            {
                if (File.Exists(withoutJs + extension))
                {
                    return withoutJs + extension;
                }
            }

            return null;
        }

        string[] candidates =
        [
            combinedPath + ".ts", combinedPath + ".tsx",
            Path.Combine(combinedPath, "index.ts"), Path.Combine(combinedPath, "index.tsx"),
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveSharedExport(DirectoryInfo root, string subpath)
    {
        string packageJsonPath = Path.Combine(root.FullName, SharedPackageJsonPath);
        JsonNode package = JsonNode.Parse(File.ReadAllText(packageJsonPath))
            ?? throw new InvalidOperationException($"{SharedPackageJsonPath} did not parse as JSON.");
        JsonObject exports = package["exports"]?.AsObject()
            ?? throw new InvalidOperationException($"{SharedPackageJsonPath} has no \"exports\" map.");

        string exactKey = subpath.Length == 0 ? "." : "./" + subpath;
        if (exports.TryGetPropertyValue(exactKey, out JsonNode? exactValue) && exactValue is not null)
        {
            return NormalizeSharedTarget(exactValue.GetValue<string>());
        }

        foreach ((string key, JsonNode? value) in exports)
        {
            if (!key.StartsWith("./", StringComparison.Ordinal) || !key.EndsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            string prefix = key[2..^2];
            if (!subpath.StartsWith(prefix + "/", StringComparison.Ordinal) || value is null)
            {
                continue;
            }

            string suffix = subpath[(prefix.Length + 1)..];
            string template = value.GetValue<string>();
            return NormalizeSharedTarget(template.Replace("*", suffix));
        }

        return null;
    }

    private static string NormalizeSharedTarget(string target)
    {
        target.StartsWith("./", StringComparison.Ordinal).ShouldBeTrue(
            $"{SharedPackageJsonPath} export target '{target}' is not a relative path this resolver understands.");
        return $"apps/shared/{target[2..]}";
    }

    // =====================================================================
    // File enumeration.
    // =====================================================================

    private static IEnumerable<string> ScannedTsFiles(DirectoryInfo root) =>
        ScannedFiles(root, ManagementSrc, [".ts", ".tsx"])
            .Concat(ScannedFiles(root, KioskSrc, [".ts", ".tsx"]))
            .Concat(ScannedFiles(root, SharedUiSrc, [".ts", ".tsx"]))
            // tailwindTheme.ts (ui/tokens/) DEFINES the role utilities — it is not a call site —
            // so it stays excluded here, unlike the CSS scan below.
            .Where(relative => !relative.Contains("/tokens/", StringComparison.Ordinal));

    /// <summary>
    /// Unlike <see cref="SharedUiTokenUsageTests"/>'s own CSS scan, <c>/tokens/</c> is NOT
    /// excluded here: <c>tokens.css</c> is exactly where <c>--duration-*</c> is declared, so it
    /// is the one file fact 5's custom-property-redeclaration check must see. Excluding it (as
    /// copied from the colour-primitive guard, where the exclusion is correct) let
    /// <c>@media (prefers-reduced-motion: reduce) { :root { --duration-enter: 0ms } }</c> added
    /// to <c>tokens.css</c> pass every fact silently — proven by counterfactual, see the PR body.
    /// </summary>
    private static IEnumerable<string> ScannedCssFiles(DirectoryInfo root) =>
        ScannedFiles(root, ManagementSrc, [".css"])
            .Concat(ScannedFiles(root, KioskSrc, [".css"]))
            .Concat(ScannedFiles(root, SharedUiSrc, [".css"]));

    private static IEnumerable<string> ScannedFiles(DirectoryInfo root, string treeRelative, string[] extensions)
    {
        string full = Path.Combine(root.FullName, treeRelative);
        if (!Directory.Exists(full))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (!extensions.Any(extension => file.EndsWith(extension, StringComparison.Ordinal)))
            {
                continue;
            }

            string relative = RepositorySource.RelativePath(root, file);
            if (relative.Contains(".test.", StringComparison.Ordinal) || relative.Contains(".spec.", StringComparison.Ordinal))
            {
                continue;
            }

            yield return relative;
        }
    }

    // =====================================================================
    // The small local CSS reader: comment-stripped, brace-depth walk.
    // =====================================================================

    private sealed record CssRule(string[] AtRuleChain, string Selector, List<(string Prop, string Value)> Declarations);

    private static List<CssRule> ParseCssRules(string css)
    {
        List<CssRule> rules = [];
        ParseCssBlock(css, 0, css.Length, [], rules);
        return rules;
    }

    private static void ParseCssBlock(string css, int start, int end, List<string> chain, List<CssRule> rules)
    {
        int i = start;
        while (i < end)
        {
            int braceIndex = css.IndexOf('{', i);
            if (braceIndex == -1 || braceIndex >= end)
            {
                break;
            }

            // The text between `i` and `braceIndex` can hold more than the prelude: any
            // `;`-terminated statement written before the first rule at this position (a leading
            // `@import '...'; @import 'tailwindcss';` — both real index.css files start this way)
            // sits there too. Only the text AFTER the last top-level `;` is the prelude; everything
            // before it is a sibling statement with no brace body, and is discarded here rather
            // than concatenated onto the prelude (which broke `StartsWith("@keyframes", ...)` for
            // exactly the block most likely to follow an `@import` chain).
            string preludeSpan = css[i..braceIndex];
            int lastTopLevelSemicolon = preludeSpan.LastIndexOf(';');
            string prelude = (lastTopLevelSemicolon >= 0 ? preludeSpan[(lastTopLevelSemicolon + 1)..] : preludeSpan)
                .Trim();

            int closeIndex = FindMatchingBrace(css, braceIndex);
            string body = css[(braceIndex + 1)..closeIndex];

            if (prelude.Length > 0)
            {
                int nestedBraceIndex = body.IndexOf('{');

                if (nestedBraceIndex == -1)
                {
                    rules.Add(new CssRule([.. chain], prelude, [.. ParseDeclarations(body)]));
                }
                else
                {
                    // A rule can carry both its own declarations AND a nested rule — native CSS
                    // nesting, valid in Tailwind v4, e.g. an ampersand-selector pseudo-class rule
                    // nested inside a component class that also sets its own transition. The
                    // text before the first nested brace is this rule's own body; recursing alone
                    // would swallow it into the nested rule's prelude (silently dropped there by
                    // the trim above, since it ends in a semicolon) and this rule would report no
                    // declarations at all.
                    string ownDeclarationsText = body[..nestedBraceIndex];
                    (string Prop, string Value)[] ownDeclarations = ParseDeclarations(ownDeclarationsText);
                    if (ownDeclarations.Length > 0)
                    {
                        rules.Add(new CssRule([.. chain], prelude, [.. ownDeclarations]));
                    }

                    List<string> newChain = [.. chain, prelude];
                    ParseCssBlock(css, braceIndex + 1, closeIndex, newChain, rules);
                }
            }

            i = closeIndex + 1;
        }
    }

    private static (string Prop, string Value)[] ParseDeclarations(string body) =>
        [
            .. body.Split(';')
                .Select(declaration => declaration.Trim())
                .Where(declaration => declaration.Length > 0 && declaration.Contains(':'))
                .Select(declaration =>
                {
                    int colon = declaration.IndexOf(':');
                    return (declaration[..colon].Trim(), declaration[(colon + 1)..].Trim());
                }),
        ];

    private static int FindMatchingBrace(string css, int openIndex)
    {
        int depth = 0;
        for (int j = openIndex; j < css.Length; j++)
        {
            if (css[j] == '{')
            {
                depth++;
            }
            else if (css[j] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return j;
                }
            }
        }

        throw new InvalidOperationException("unbalanced braces in CSS.");
    }

    /// <summary>
    /// Exact match only — never a substring. The pulse's rule is either its base selector
    /// (<c>.ssE-overlay-highlight</c>, exactly, covering both the plain rule and its
    /// reduced-motion redefinition, which share the same selector) or its own
    /// <c>@keyframes</c> name (<c>ssE-overlay-highlight-pulse</c>, exactly). A substring match
    /// would also exempt <c>.ssE-overlay-highlight-banner</c> or a differently-suffixed
    /// <c>@keyframes ssE-overlay-highlight-flash</c> — neither is the allowlisted rule.
    /// </summary>
    private static bool IsAllowlistedPulseRule(
        (string RelativePath, string Match, string Reason)[] allowlist, string relativePath, CssRule rule) =>
        allowlist.Any(entry => entry.RelativePath == relativePath)
        && (rule.Selector.Trim() == PulseSelector || KeyframeNameFromChain(rule.AtRuleChain) == PulseKeyframeName);

    private static bool IsReducedMotionMediaPrelude(string prelude) =>
        prelude.TrimStart().StartsWith("@media", StringComparison.Ordinal)
        && prelude.Contains("prefers-reduced-motion", StringComparison.Ordinal)
        && prelude.Contains("reduce", StringComparison.Ordinal);

    private static string? KeyframeNameFromChain(string[] atRuleChain)
    {
        foreach (string part in atRuleChain)
        {
            string trimmed = part.TrimStart();
            if (trimmed.StartsWith("@keyframes", StringComparison.Ordinal))
            {
                return trimmed["@keyframes".Length..].Trim();
            }
        }

        return null;
    }

    private static Dictionary<string, string> RootDeclarations(string css)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (CssRule rule in ParseCssRules(css))
        {
            if (rule.AtRuleChain.Length != 0 || rule.Selector.Trim() != ":root")
            {
                continue;
            }

            foreach ((string prop, string value) in rule.Declarations)
            {
                map[prop] = value;
            }
        }

        return map;
    }

    private enum CommentStyle
    {
        CssOnly,
        TsAndBlock,
    }

    private static string StripComments(string text, CommentStyle style)
    {
        string withoutBlockComments = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return style == CommentStyle.TsAndBlock
            ? Regex.Replace(withoutBlockComments, @"//[^\n]*", string.Empty)
            : withoutBlockComments;
    }
}
