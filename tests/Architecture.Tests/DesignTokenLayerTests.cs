using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards ADR-0148's two-layer token file (spec 256, issue #2332): one file both
/// <c>apps/management-web</c> and <c>apps/kiosk-web</c> import, carrying a primitive
/// OKLCH scale and a semantic layer that cites it, in every category the issue names.
///
/// <para>
/// Follows <see cref="ContainerImagePinTests"/>'s shape — reads the tree from disk,
/// a comment-stripped scan, failure messages that say what to do — but locates the
/// token file <b>by following each app's first <c>@import</c></b> rather than by a
/// hard-coded name. On <c>develop</c> that import resolves to <c>colors.css</c>,
/// which is seven raw-hex properties and nothing else, so most facts below read
/// real content and fail on it rather than on a missing file (spec §6: "a red from
/// a missing file would be the weak red spec §6 forbids").
/// </para>
///
/// <para>
/// <b>Declared green in advance (spec §6), and not phase-4a evidence</b>: facts 1,
/// 7, 8 and 9. Fact 10 is also green on <c>develop</c>, but for a different reason —
/// it is a vacuous pass (nothing anywhere cites a primitive-shaped name, because no
/// primitive exists yet to cite), not a declared pin. Its counterfactual is proved
/// separately (issue #2332 phase-4a report): a <c>var(--gray-900)</c> planted
/// outside this file must fail it even though <c>--gray-900</c> is not declared
/// anywhere — the scan matches the <em>shape</em> of a primitive name, not a lookup
/// against declared ones, precisely so that proof is possible.
/// </para>
/// </summary>
public class DesignTokenLayerTests
{
    private const string ManagementIndexCss = "apps/management-web/src/styles/index.css";
    private const string KioskIndexCss = "apps/kiosk-web/src/styles/index.css";
    private const string ManagementTailwindConfig = "apps/management-web/tailwind.config.ts";
    private const string KioskTailwindConfig = "apps/kiosk-web/tailwind.config.ts";
    private const string SharedTailwindThemeModule = "apps/shared/src/ui/tokens/tailwindTheme.ts";
    private const string SharedPackageJson = "apps/shared/package.json";

    private static readonly string[] Categories =
        ["color", "space", "text", "font", "tracking", "radius", "shadow", "duration", "ease", "z"];

    private static readonly string[] BridgeNames =
        ["--default-transition-duration", "--default-transition-timing-function"];

    /// <summary>
    /// <c>--&lt;hue&gt;-&lt;step&gt;</c> or <c>--black</c>/<c>--white</c>, and
    /// nothing else (plan.md §1): one lower-case word, an optional single numeric
    /// suffix, and NOT one of the ten category prefixes. Matched by shape, not by
    /// a list of declared names — see the class remarks on fact 10's counterfactual.
    /// </summary>
    private static readonly Regex PrimitiveName = new(
        @"^--(?!(?:color|space|text|font|tracking|radius|shadow|duration|ease|z)-)[a-z]+(?:-\d+)?$",
        RegexOptions.Compiled);

    private static readonly Regex SimpleVarValue = new(@"^var\(--[A-Za-z0-9-]+\)$", RegexOptions.Compiled);

    private static readonly Regex ColorMixValue = new(
        @"^color-mix\(in oklch,\s*var\(--[A-Za-z0-9-]+\)\s+\d+%,\s*(?:var\(--[A-Za-z0-9-]+\)|transparent)\)$",
        RegexOptions.Compiled);

    private static readonly Regex ColorLiteral = new(
        @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(",
        RegexOptions.Compiled);

    private static readonly Regex VarReference = new(@"var\(\s*(?<name>--[A-Za-z0-9-]+)", RegexOptions.Compiled);

    // The triad's rendered sRGB (ADR-0146, spec §6 pin). Constants, not read back
    // from the file, so the subject can change without the assertion changing.
    private const string GreenAccentActive = "#00c853"; // ADR-0146: the triad's "go" stop
    private const string RedAccentFault = "#ff5252"; // ADR-0146: the triad's "fault" stop
    private const string AmberAccentWarning = "#ffab40"; // ADR-0146: the triad's "warning" stop

    // The kept dark ground (ADR-0146: "the wall does not change colour", spec §6 pin).
    private const string DarkGroundBase = "#0b0d10"; // ADR-0146: --color-bg-base kept
    private const string DarkGroundElevated = "#14171c"; // ADR-0146: --color-bg-elevated kept

    /// <summary>Fact 1 (green pin). Both apps' first <c>@import</c> names one file.</summary>
    [Fact]
    public void Both_surfaces_import_the_same_token_file_first()
    {
        DirectoryInfo root = RepositorySource.Root();

        FileInfo management = TokenFileImportedBy(root, ManagementIndexCss);
        FileInfo kiosk = TokenFileImportedBy(root, KioskIndexCss);

        string managementRelative = RepositorySource.RelativePath(root, management.FullName);
        string kioskRelative = RepositorySource.RelativePath(root, kiosk.FullName);

        kioskRelative.ShouldBe(
            managementRelative,
            $"{ManagementIndexCss}'s first @import resolves to {managementRelative}, but "
            + $"{KioskIndexCss}'s resolves to {kioskRelative} — both apps must share exactly one token file.");
    }

    /// <summary>Fact 2. Red on develop: only <c>color</c> is present.</summary>
    [Fact]
    public void The_token_file_declares_every_category()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        Dictionary<string, string> rootMap = RootMap(ParseDeclarations(ReadCss(tokenFile)));

        string[] missing =
        [
            .. Categories.Where(category => !rootMap.Keys.Any(
                name => name.StartsWith($"--{category}-", StringComparison.Ordinal))),
        ];

        missing.ShouldBeEmpty(
            $"{tokenFile.Name}'s :root declares no token in {string.Join(", ", missing)} — "
            + "ADR-0148 names ten categories (color, space, text, font, tracking, radius, shadow, "
            + "duration, ease, z) and each needs at least one name.");
    }

    /// <summary>
    /// Fact 3. Every declared custom property, anywhere in the file, is one of: a
    /// token (a known category prefix), a primitive (the shape above), or one of
    /// the two named bridges — nothing uncategorised.
    /// </summary>
    [Fact]
    public void Every_name_is_a_token_a_primitive_or_a_named_bridge()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));

        Declaration[] unclassified =
        [
            .. declarations.Where(declaration =>
                !IsCategoryToken(declaration.Name)
                && !PrimitiveName.IsMatch(declaration.Name)
                && !BridgeNames.Contains(declaration.Name, StringComparer.Ordinal)),
        ];

        unclassified.ShouldBeEmpty(
            $"{tokenFile.Name} declares a name that is none of token, primitive or named bridge: "
            + string.Join(", ", unclassified.Select(declaration => $"{declaration.Name} ({declaration.Selector})")));
    }

    /// <summary>
    /// Fact 4. Red on develop: zero primitives exist yet — the file is all
    /// semantic-shaped names — so the "at least one primitive" requirement fails
    /// before the oklch/root-only checks are even reached.
    /// </summary>
    [Fact]
    public void Primitives_are_oklch_literals_declared_only_in_root()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));

        Declaration[] primitives = [.. declarations.Where(declaration => PrimitiveName.IsMatch(declaration.Name))];

        primitives.ShouldNotBeEmpty(
            $"{tokenFile.Name} declares no primitive-named property yet (a `--<hue>-<step>` or "
            + "--black/--white). ADR-0148's primitive layer — the gray and cyan ramps, the triad "
            + "stops, black and white — is required before the semantic layer can cite it.");

        Declaration[] notOklch =
        [
            .. primitives.Where(declaration => !Regex.IsMatch(declaration.Value, @"^oklch\(", RegexOptions.Compiled)),
        ];

        notOklch.ShouldBeEmpty(
            "these primitives are not oklch() literals: "
            + string.Join(", ", notOklch.Select(declaration => $"{declaration.Name}: {declaration.Value}")));

        Declaration[] outsideRoot = [.. primitives.Where(declaration => !IsRootSelector(declaration.Selector))];

        outsideRoot.ShouldBeEmpty(
            "these primitives are declared outside :root — a theme must never redeclare a primitive "
            + "(ADR-0148, plan.md §1): "
            + string.Join(", ", outsideRoot.Select(declaration => $"{declaration.Name} in {declaration.Selector}")));
    }

    /// <summary>Fact 5. Red on develop: every semantic colour is a raw hex literal.</summary>
    [Fact]
    public void Semantic_colours_cite_the_scale_never_a_literal()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));

        Declaration[] semanticColours =
        [
            .. declarations.Where(declaration => declaration.Name.StartsWith("--color-", StringComparison.Ordinal)),
        ];

        Declaration[] invalid = [.. semanticColours.Where(declaration => !ValidSemanticColorValue(declaration.Value))];

        invalid.ShouldBeEmpty(
            "these --color-* tokens are not `var(<primitive|semantic>)` or "
            + "`color-mix(in oklch, <those> N%, <those|transparent>)`: "
            + string.Join(
                Environment.NewLine,
                invalid.Select(declaration => $"  {declaration.Selector} {declaration.Name}: {declaration.Value}")));
    }

    /// <summary>
    /// Fact 6. Red on develop: no <c>[data-theme='high-contrast']</c> block exists
    /// at all, which fails before either theme's declared names are checked
    /// against :root.
    /// </summary>
    [Fact]
    public void A_theme_redeclares_only_semantic_names_root_already_has()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));

        HashSet<string> rootNames = [.. RootMap(declarations).Keys];

        Declaration[] light = [.. declarations.Where(declaration => IsLightTheme(declaration.Selector))];
        Declaration[] highContrast = [.. declarations.Where(declaration => IsHighContrastTheme(declaration.Selector))];

        light.ShouldNotBeEmpty(
            $"{tokenFile.Name} has no [data-theme='light'] block — ADR-0146's light theme remapping "
            + "must exist even though nothing sets data-theme='light' yet (spec §3).");
        highContrast.ShouldNotBeEmpty(
            $"{tokenFile.Name} has no [data-theme='high-contrast'] block — same as the light theme.");

        foreach ((Declaration[] themeDeclarations, string themeName) in new[]
                 {
                     (light, "light"),
                     (highContrast, "high-contrast"),
                 })
        {
            Declaration[] redeclaredPrimitives =
                [.. themeDeclarations.Where(declaration => PrimitiveName.IsMatch(declaration.Name))];

            redeclaredPrimitives.ShouldBeEmpty(
                $"[data-theme='{themeName}'] redeclares a primitive, which only :root may declare: "
                + string.Join(", ", redeclaredPrimitives.Select(declaration => declaration.Name)));

            Declaration[] unknownToRoot =
                [.. themeDeclarations.Where(declaration => !rootNames.Contains(declaration.Name))];

            unknownToRoot.ShouldBeEmpty(
                $"[data-theme='{themeName}'] declares a name :root never declares: "
                + string.Join(", ", unknownToRoot.Select(declaration => declaration.Name))
                + " — a theme may only remap a name that already exists.");
        }
    }

    /// <summary>Fact 7 (green pin). The triad's rendered sRGB does not move.</summary>
    [Fact]
    public void The_triad_keeps_its_rendered_values()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        Dictionary<string, string> map = RootMap(ParseDeclarations(ReadCss(tokenFile)));

        AssertResolvesTo(map, "--color-accent-active", GreenAccentActive, tokenFile);
        AssertResolvesTo(map, "--color-accent-fault", RedAccentFault, tokenFile);
        AssertResolvesTo(map, "--color-accent-warning", AmberAccentWarning, tokenFile);
    }

    /// <summary>Fact 8 (green pin). The dark ground's rendered sRGB does not move.</summary>
    [Fact]
    public void The_dark_ground_keeps_its_rendered_values()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        Dictionary<string, string> map = RootMap(ParseDeclarations(ReadCss(tokenFile)));

        AssertResolvesTo(map, "--color-bg-base", DarkGroundBase, tokenFile);
        AssertResolvesTo(map, "--color-bg-elevated", DarkGroundElevated, tokenFile);
    }

    /// <summary>Fact 9 (green pin). True today by absence (spec §6).</summary>
    [Fact]
    public void No_blur_token_exists()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        string css = ReadCss(tokenFile);
        List<Declaration> declarations = ParseDeclarations(css);

        Declaration[] blurNames =
            [.. declarations.Where(declaration => declaration.Name.StartsWith("--blur", StringComparison.Ordinal))];

        blurNames.ShouldBeEmpty(
            $"{tokenFile.Name} declares a --blur* token: {string.Join(", ", blurNames.Select(d => d.Name))} — "
            + "ADR-0146 prohibits blur on the wall; there is no blur token, deliberately.");

        css.Contains("backdrop-filter", StringComparison.Ordinal).ShouldBeFalse(
            $"{tokenFile.Name} contains 'backdrop-filter' — no token value may reach for it.");
        css.Contains("blur(", StringComparison.Ordinal).ShouldBeFalse(
            $"{tokenFile.Name} contains 'blur(' — no token value may reach for it.");
    }

    /// <summary>
    /// Fact 10. Green on develop, but vacuously (see class remarks) — the scan
    /// matches every <c>var(--&lt;primitive-shaped-name&gt;)</c> outside the token
    /// file, by shape, regardless of whether that name is declared anywhere.
    /// </summary>
    [Fact]
    public void Nothing_outside_the_token_file_cites_a_primitive()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        string tokenFileRelative = RepositorySource.RelativePath(root, tokenFile.FullName);

        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root, tokenFileRelative))
        {
            string path = Path.Combine(root.FullName, relativePath);
            string text = StripComments(File.ReadAllText(path), relativePath.EndsWith(".css", StringComparison.Ordinal) ? CommentStyle.CssOnly : CommentStyle.TsAndBlock);

            foreach (Match match in VarReference.Matches(text))
            {
                string name = match.Groups["name"].Value;
                if (PrimitiveName.IsMatch(name))
                {
                    violations.Add($"{relativePath}: var({name})");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a primitive is cited outside the token file — components must cite the semantic layer only "
            + "(ADR-0148):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 11. Red on develop: both configs map colours inline with `var(--...)`
    /// literals and import nothing.
    /// </summary>
    [Fact]
    public void Both_configs_import_the_shared_theme_and_map_nothing_locally()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> problems = [];

        foreach (string config in new[] { ManagementTailwindConfig, KioskTailwindConfig })
        {
            string path = Path.Combine(root.FullName, config);
            File.Exists(path).ShouldBeTrue($"expected {config} at {path}.");

            string source = StripComments(File.ReadAllText(path), CommentStyle.TsAndBlock);

            bool importsSharedTheme = Regex.IsMatch(
                source,
                @"import\s*\{[^}]*\btailwindTheme\b[^}]*\}\s*from\s*['""][^'""]*shared/src/ui/tokens/tailwindTheme['""]");

            if (!importsSharedTheme)
            {
                problems.Add(
                    $"{config} has no `import {{ tailwindTheme }} from '…/shared/src/ui/tokens/tailwindTheme'` — "
                    + "both configs must import the one shared theme object (plan.md §4.1).");
            }

            if (source.Contains("var(--", StringComparison.Ordinal))
            {
                problems.Add(
                    $"{config} contains a `var(--` literal — a config must not map a token locally; "
                    + "it imports the shared theme instead.");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Fact 12. The one fact whose red on develop is a missing file
    /// (<c>tailwindTheme.ts</c> does not exist), acceptable because fact 11 already
    /// carries the discriminating red for the same story (plan.md §5.1).
    /// </summary>
    [Fact]
    public void Every_token_the_theme_cites_is_declared_and_semantic()
    {
        DirectoryInfo root = RepositorySource.Root();
        string themePath = Path.Combine(root.FullName, SharedTailwindThemeModule);

        File.Exists(themePath).ShouldBeTrue(
            $"expected {SharedTailwindThemeModule} — the one theme object both tailwind.config.ts files "
            + "import (plan.md §4.1). It does not exist yet.");

        string source = StripComments(File.ReadAllText(themePath), CommentStyle.TsAndBlock);
        HashSet<string> citedNames =
            [.. VarReference.Matches(source).Select(match => match.Groups["name"].Value)];

        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        HashSet<string> declaredNames = [.. declarations.Select(declaration => declaration.Name)];

        List<string> problems = [];

        foreach (string name in citedNames)
        {
            if (!declaredNames.Contains(name))
            {
                problems.Add($"{SharedTailwindThemeModule} cites {name}, which {tokenFile.Name} never declares.");
                continue;
            }

            if (PrimitiveName.IsMatch(name))
            {
                problems.Add(
                    $"{SharedTailwindThemeModule} cites the primitive {name} directly — the theme must "
                    + "cite semantic tokens only (plan.md §1).");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    // =====================================================================
    // Resolution: var() chains and OKLCH/hex → 8-bit sRGB (plan.md §5.1 fact 7).
    // =====================================================================

    private static void AssertResolvesTo(Dictionary<string, string> map, string name, string expectedHex, FileInfo tokenFile)
    {
        map.ContainsKey(name).ShouldBeTrue($"{name} is not declared in {tokenFile.Name}'s :root.");

        (int r, int g, int b) = ResolveToSrgb(name, map, []);
        string actualHex = $"#{r:x2}{g:x2}{b:x2}";

        actualHex.ShouldBe(
            expectedHex,
            $"{name} resolves to {actualHex}, expected {expectedHex} (ADR-0146: this value does not move).");
    }

    private static (int R, int G, int B) ResolveToSrgb(string name, Dictionary<string, string> map, HashSet<string> seen)
    {
        map.TryGetValue(name, out string? value).ShouldBeTrue($"{name} has no declaration to resolve.");

        string trimmed = value.ShouldNotBeNull().Trim();
        Match varMatch = SimpleVarValue.IsMatch(trimmed) ? VarReference.Match(trimmed) : Match.Empty;

        if (varMatch.Success)
        {
            string next = varMatch.Groups["name"].Value;
            seen.Add(name).ShouldBeTrue($"circular var() reference resolving {name} → {next}.");
            return ResolveToSrgb(next, map, seen);
        }

        return ParseColorLiteral(trimmed, name);
    }

    private static (int R, int G, int B) ParseColorLiteral(string value, string forName)
    {
        Match hex = Regex.Match(value, @"^#(?<hex>[0-9a-fA-F]{6})$");
        if (hex.Success)
        {
            string h = hex.Groups["hex"].Value;
            return (
                Convert.ToInt32(h[..2], 16),
                Convert.ToInt32(h[2..4], 16),
                Convert.ToInt32(h[4..6], 16));
        }

        Match oklch = Regex.Match(value, @"^oklch\(\s*(?<l>[\d.]+)%\s+(?<c>[\d.]+)\s+(?<h>[\d.]+)\s*\)$");
        if (oklch.Success)
        {
            double l = double.Parse(oklch.Groups["l"].Value, CultureInfo.InvariantCulture);
            double c = double.Parse(oklch.Groups["c"].Value, CultureInfo.InvariantCulture);
            double h = double.Parse(oklch.Groups["h"].Value, CultureInfo.InvariantCulture);
            return OklchToSrgb(l, c, h);
        }

        throw new InvalidOperationException(
            $"cannot resolve '{forName}' to a literal colour — its value '{value}' is neither a hex nor an "
            + "oklch() literal. This resolver serves the two pinned facts (the triad, the dark ground), which "
            + "resolve through a var() chain to a plain primitive; it does not evaluate color-mix().");
    }

    /// <summary>
    /// CSS Color 4's OKLab → linear sRGB matrices, verified against the plan.md §2.1
    /// table (green-500/red-500/amber-500/gray-900/gray-950 all round-trip to their
    /// listed hex).
    /// </summary>
    private static (int R, int G, int B) OklchToSrgb(double lightnessPercent, double chroma, double hueDegrees)
    {
        double l = lightnessPercent / 100.0;
        double hueRadians = hueDegrees * Math.PI / 180.0;
        double a = chroma * Math.Cos(hueRadians);
        double b = chroma * Math.Sin(hueRadians);

        double lPrime = l + (0.3963377774 * a) + (0.2158037573 * b);
        double mPrime = l - (0.1055613458 * a) - (0.0638541728 * b);
        double sPrime = l - (0.0894841775 * a) - (1.2914855480 * b);

        double lCubed = lPrime * lPrime * lPrime;
        double mCubed = mPrime * mPrime * mPrime;
        double sCubed = sPrime * sPrime * sPrime;

        double rLinear = (4.0767416621 * lCubed) - (3.3077115913 * mCubed) + (0.2309699292 * sCubed);
        double gLinear = (-1.2684380046 * lCubed) + (2.6097574011 * mCubed) - (0.3413193965 * sCubed);
        double bLinear = (-0.0041960863 * lCubed) - (0.7034186147 * mCubed) + (1.7076147010 * sCubed);

        return (GammaEncodeTo255(rLinear), GammaEncodeTo255(gLinear), GammaEncodeTo255(bLinear));
    }

    private static int GammaEncodeTo255(double linear)
    {
        double clampedLinear = Math.Clamp(linear, 0.0, 1.0);
        double encoded = clampedLinear <= 0.0031308
            ? 12.92 * clampedLinear
            : (1.055 * Math.Pow(clampedLinear, 1.0 / 2.4)) - 0.055;

        return (int)Math.Round(Math.Clamp(encoded, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);
    }

    // =====================================================================
    // Parsing: comment-stripped CSS into (selector, name, value) declarations.
    // =====================================================================

    private static bool ValidSemanticColorValue(string value) =>
        !ColorLiteral.IsMatch(value) && (SimpleVarValue.IsMatch(value) || ColorMixValue.IsMatch(value));

    private static bool IsCategoryToken(string name) =>
        Categories.Any(category => name.StartsWith($"--{category}-", StringComparison.Ordinal));

    private static bool IsRootSelector(string selector) => selector.Trim() == ":root";

    private static bool IsLightTheme(string selector) =>
        Regex.IsMatch(selector, @"\[data-theme\s*=\s*['""]light['""]\]");

    private static bool IsHighContrastTheme(string selector) =>
        Regex.IsMatch(selector, @"\[data-theme\s*=\s*['""]high-contrast['""]\]");

    private static Dictionary<string, string> RootMap(List<Declaration> declarations) =>
        declarations
            .Where(declaration => IsRootSelector(declaration.Selector))
            .ToDictionary(declaration => declaration.Name, declaration => declaration.Value, StringComparer.Ordinal);

    private static List<Declaration> ParseDeclarations(string css)
    {
        List<Declaration> declarations = [];

        foreach (Match block in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline))
        {
            string selector = block.Groups["sel"].Value.Trim();

            foreach (Match declaration in Regex.Matches(
                         block.Groups["body"].Value,
                         @"(?<name>--[A-Za-z][A-Za-z0-9-]*)\s*:\s*(?<value>[^;]+);"))
            {
                declarations.Add(new Declaration(
                    selector,
                    declaration.Groups["name"].Value,
                    declaration.Groups["value"].Value.Trim()));
            }
        }

        return declarations;
    }

    private static string ReadCss(FileInfo tokenFile) => StripComments(File.ReadAllText(tokenFile.FullName), CommentStyle.CssOnly);

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

    /// <summary>
    /// Files a primitive must never be cited from outside the token file (fact
    /// 10): both apps' <c>src</c> trees, <c>apps/shared/src</c> (which also holds
    /// the token file itself — excluded by path — and, once it exists,
    /// <c>tailwindTheme.ts</c>), and both <c>tailwind.config.ts</c> files.
    /// </summary>
    private static IEnumerable<string> ScannedFiles(DirectoryInfo root, string excludeRelative)
    {
        string[] scannedTrees = ["apps/management-web/src", "apps/kiosk-web/src", "apps/shared/src"];

        foreach (string tree in scannedTrees)
        {
            string full = Path.Combine(root.FullName, tree);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".ts", StringComparison.Ordinal)
                    && !file.EndsWith(".tsx", StringComparison.Ordinal)
                    && !file.EndsWith(".css", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = RepositorySource.RelativePath(root, file);
                if (relative == excludeRelative)
                {
                    continue;
                }

                yield return relative;
            }
        }

        yield return ManagementTailwindConfig;
        yield return KioskTailwindConfig;
    }

    /// <summary>
    /// Resolves an app's index.css first <c>@import</c> to a file on disk — by
    /// following the import, never by assuming a filename. A bare relative import
    /// resolves against the importing file's directory; a
    /// <c>@smart-sentinel-eye/shared/...</c> import resolves through
    /// <c>apps/shared/package.json</c>'s <c>exports</c> map, the same resolution
    /// a bundler performs, so the same code finds <c>colors.css</c> today and
    /// <c>tokens.css</c> once T010 renames it.
    /// </summary>
    private static FileInfo TokenFileImportedBy(DirectoryInfo root, string appIndexCssRelativePath)
    {
        string indexCssPath = Path.Combine(root.FullName, appIndexCssRelativePath);
        File.Exists(indexCssPath).ShouldBeTrue($"expected {appIndexCssRelativePath} at {indexCssPath}.");

        string css = StripComments(File.ReadAllText(indexCssPath), CommentStyle.CssOnly);
        Match import = Regex.Match(css, @"@import\s+'(?<path>[^']+)'\s*;");

        import.Success.ShouldBeTrue(
            $"{appIndexCssRelativePath} has no `@import '...'` as its first statement — the token file "
            + "is located by following this import, not by a hard-coded name.");

        string importPath = import.Groups["path"].Value;
        const string SharedPrefix = "@smart-sentinel-eye/shared/";

        if (importPath.StartsWith(SharedPrefix, StringComparison.Ordinal))
        {
            return ResolveSharedExport(root, importPath[SharedPrefix.Length..], appIndexCssRelativePath, importPath);
        }

        string directory = Path.GetDirectoryName(indexCssPath)!;
        return new FileInfo(Path.GetFullPath(Path.Combine(directory, importPath)));
    }

    private static FileInfo ResolveSharedExport(DirectoryInfo root, string subpath, string appIndexCssRelativePath, string importPath)
    {
        string packageJsonPath = Path.Combine(root.FullName, SharedPackageJson);
        File.Exists(packageJsonPath).ShouldBeTrue($"expected {SharedPackageJson} at {packageJsonPath}.");

        JsonNode package = JsonNode.Parse(File.ReadAllText(packageJsonPath))
            ?? throw new InvalidOperationException($"{SharedPackageJson} did not parse as JSON.");
        JsonObject exports = package["exports"]?.AsObject()
            ?? throw new InvalidOperationException($"{SharedPackageJson} has no \"exports\" map.");

        foreach ((string key, JsonNode? value) in exports)
        {
            if (!key.StartsWith("./", StringComparison.Ordinal) || !key.EndsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            string exportPrefix = key[2..^2];
            if (!subpath.StartsWith(exportPrefix + "/", StringComparison.Ordinal))
            {
                continue;
            }

            string target = value?.GetValue<string>()
                ?? throw new InvalidOperationException($"{SharedPackageJson}'s export \"{key}\" has no string target.");
            target.StartsWith("./", StringComparison.Ordinal).ShouldBeTrue(
                $"{SharedPackageJson}'s export \"{key}\" → \"{target}\" is not a relative path this resolver understands.");
            target.EndsWith("/*", StringComparison.Ordinal).ShouldBeTrue(
                $"{SharedPackageJson}'s export \"{key}\" → \"{target}\" is not a wildcard export this resolver understands.");

            string suffix = subpath[(exportPrefix.Length + 1)..];
            string targetPrefix = target[2..^2];
            string resolvedRelative = $"apps/shared/{targetPrefix}/{suffix}";

            return new FileInfo(Path.Combine(root.FullName, resolvedRelative));
        }

        throw new InvalidOperationException(
            $"{appIndexCssRelativePath} imports '{importPath}', but no wildcard export in {SharedPackageJson} "
            + $"maps './{subpath}'.");
    }

    /// <summary>The token file, resolved through management-web's import (fact 1 proves kiosk-web agrees).</summary>
    private static FileInfo TokenFile(DirectoryInfo root) => TokenFileImportedBy(root, ManagementIndexCss);

    private sealed record Declaration(string Selector, string Name, string Value);
}
