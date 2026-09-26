using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards §I (no outbound internet dependency) for fonts: issue #2333, spec 261.
/// The product self-hosts IBM Plex; nothing under <c>apps/**</c> may reach a CDN
/// for a font, and no self-hosted face may defer to a machine-installed copy.
///
/// <para>
/// Shape follows <see cref="ContainerImagePinTests"/>: reads the tree from disk
/// via <see cref="RepositorySource"/>, a comment-stripped scan, forward-slash
/// paths, failure messages that say what to do.
/// </para>
///
/// <para>
/// <b>This bans a category, never a value.</b> No assertion here names a host —
/// <c>fonts.googleapis.com</c> appears nowhere in this file. Any absolute URL is
/// the violation, because §I bans every outbound fetch and fonts are the case
/// that motivated the guard (spec 261 §1).
/// </para>
///
/// <para>
/// <b>What this scan cannot see (spec 261 plan.md §6.1):</b> a stylesheet or
/// <c>FontFace</c> built in TypeScript at runtime (nothing here evaluates JS); CSS
/// a dependency brings in from <c>node_modules</c> (excluded by path, and rightly
/// so — this scan is the authority for what this repository writes, not for what
/// it depends on); and a <c>url()</c> assembled from a variable rather than
/// written as a literal. <c>fonts.build.test.ts</c>'s <c>B3</c> (the built output)
/// and <c>e2e/self-hosted-fonts.spec.ts</c>'s <c>E1</c> (the network the running
/// page actually uses) are the authorities for those, exactly as
/// <c>AppHostContainerImagePinTests</c> is for what <c>ContainerImagePinTests</c>
/// cannot see.
/// </para>
/// </summary>
public class ExternalFontHostTests
{
    private const string AppsTree = "apps";

    private static readonly string[] ExcludedSegments = ["node_modules", "dist", ".vite"];

    /// <summary>
    /// A <c>url(...)</c> function's target, single- or double-quoted or bare.
    /// Captures the raw target text; the caller decides what "absolute" means.
    /// </summary>
    private static readonly Regex UrlFunction = new(
        @"url\(\s*(?:'(?<target>[^']*)'|""(?<target>[^""]*)""|(?<target>[^'"")\s][^)]*?))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex ImportStatement = new(
        @"@import\s+(?:url\(\s*(?:'(?<target>[^']*)'|""(?<target>[^""]*)""|(?<target>[^'"")\s][^)]*?))\s*\)"
        + @"|'(?<target>[^']*)'|""(?<target>[^""]*)"")",
        RegexOptions.Compiled);

    private static readonly Regex FontFaceBlock = new(
        @"@font-face\s*\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SrcDeclaration = new(
        @"(?<![-\w])src\s*:\s*(?<value>[^;]+);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LinkTag = new(
        @"<link\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HrefAttribute = new(
        @"href\s*=\s*(?:'(?<value>[^']*)'|""(?<value>[^""]*)"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Fact G1. Red on develop is not expected — this is declared vacuous-green
    /// in advance (spec 261 §6): nothing under <c>apps/**</c> declares a font yet,
    /// so there is nothing to be absolute. Proved live by a planted counterfactual
    /// (plan.md §6.5 #1), quoted in the PR, not by this test going red on develop.
    /// </summary>
    [Fact]
    public void No_stylesheet_under_apps_references_an_absolute_url()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in CssFiles(root))
        {
            string css = StripCssComments(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in UrlFunction.Matches(css))
            {
                string target = match.Groups["target"].Value;
                if (IsAbsolute(target))
                {
                    violations.Add($"{relativePath}:{LineOf(css, match.Index)} url({target})");
                }
            }

            foreach (Match match in ImportStatement.Matches(css))
            {
                string target = match.Groups["target"].Value;
                if (IsAbsolute(target))
                {
                    violations.Add($"{relativePath}:{LineOf(css, match.Index)} @import {target}");
                }
            }
        }

        violations.ShouldBeEmpty(
            $"{violations.Count} stylesheet reference(s) under {AppsTree}/ reach for an absolute URL, which "
            + "means a browser fetches it from somewhere other than this app's own origin — the fleet cannot "
            + $"rely on that reaching the fab LAN (constitution §I):{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact G2. Same declared-vacuous-green status as G1 (spec 261 §6):
    /// <c>apps/*/index.html</c> carries no font-related <c>&lt;link&gt;</c> yet.
    /// Proved live by a planted counterfactual (plan.md §6.5 #2).
    /// </summary>
    [Fact]
    public void No_index_html_link_loads_from_an_absolute_url()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in IndexHtmlFiles(root))
        {
            string html = File.ReadAllText(Path.Combine(root.FullName, relativePath));

            foreach (Match link in LinkTag.Matches(html))
            {
                Match href = HrefAttribute.Match(link.Value);
                if (!href.Success)
                {
                    continue;
                }

                string target = href.Groups["value"].Value;
                if (IsAbsolute(target))
                {
                    violations.Add($"{relativePath}: <link ... href=\"{target}\">");
                }
            }
        }

        violations.ShouldBeEmpty(
            $"{violations.Count} <link> element(s) in an apps/*/index.html load from an absolute URL — the "
            + "same outbound-fetch violation as a stylesheet's url(), just written in HTML "
            + $"(constitution §I):{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact G3. <b>Red on develop</b>: no <c>@font-face</c> exists anywhere under
    /// <c>apps/**</c> yet, so the self-check below is what actually fails —
    /// deliberately, per spec 261 §6, rather than a silent vacuous pass.
    /// </summary>
    [Fact]
    public void Every_font_face_url_names_a_file_that_exists()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<(string RelativePath, string Url)> urlSources = [];

        foreach (string relativePath in CssFiles(root))
        {
            string css = StripCssComments(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match face in FontFaceBlock.Matches(css))
            {
                Match src = SrcDeclaration.Match(face.Groups["body"].Value);
                if (!src.Success)
                {
                    continue;
                }

                foreach (Match url in UrlFunction.Matches(src.Groups["value"].Value))
                {
                    urlSources.Add((relativePath, url.Groups["target"].Value));
                }
            }
        }

        urlSources.ShouldNotBeEmpty(
            $"no @font-face url() source was found anywhere under {AppsTree}/ — the scan is broken, not the "
            + "code. A source-scanning guard that matches nothing passes, and a passing guard that checks "
            + "nothing is indistinguishable from one that holds.");

        List<string> missing = [];

        foreach ((string relativePath, string url) in urlSources)
        {
            if (IsAbsolute(url))
            {
                // G1 already fails a build for this; this fact is only about
                // whether a *relative* reference resolves to a real file.
                continue;
            }

            string stylesheetDirectory = Path.GetDirectoryName(Path.Combine(root.FullName, relativePath))!;
            string resolved = Path.GetFullPath(Path.Combine(stylesheetDirectory, url));

            if (!File.Exists(resolved))
            {
                missing.Add($"{relativePath}: url({url}) → {RepositorySource.RelativePath(root, resolved)}");
            }
        }

        missing.ShouldBeEmpty(
            $"{missing.Count} @font-face url() source(s) name a file that does not exist on disk:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(m => $"  {m}")));
    }

    /// <summary>
    /// Fact G4. Declared vacuous-green in advance (spec 261 §6) — no
    /// <c>@font-face</c> exists yet, so no rule can mix <c>local()</c> and
    /// <c>url()</c>. Proved live by a planted counterfactual (plan.md §6.5 #3):
    /// IBM's own upstream CSS does exactly this, which is why the rule exists.
    /// </summary>
    [Fact]
    public void No_font_face_mixes_local_and_url_sources()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in CssFiles(root))
        {
            string css = StripCssComments(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match face in FontFaceBlock.Matches(css))
            {
                Match src = SrcDeclaration.Match(face.Groups["body"].Value);
                if (!src.Success)
                {
                    continue;
                }

                string value = src.Groups["value"].Value;
                bool hasUrl = UrlFunction.IsMatch(value);
                bool hasLocal = value.Contains("local(", StringComparison.Ordinal);

                if (hasUrl && hasLocal)
                {
                    violations.Add($"{relativePath}: src: {value.Trim()}");
                }
            }
        }

        violations.ShouldBeEmpty(
            $"{violations.Count} @font-face rule(s) mix a url() source with a local() source. A machine with "
            + "some other build of that family already installed would render its own copy instead of the "
            + "bytes this repository ships — the exact fleet disagreement self-hosting exists to end "
            + $"(ADR-0147):{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    private static bool IsAbsolute(string target) =>
        target.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("//", StringComparison.Ordinal);

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string StripCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static IEnumerable<string> CssFiles(DirectoryInfo root)
    {
        string appsPath = Path.Combine(root.FullName, AppsTree);

        foreach (string file in Directory.EnumerateFiles(appsPath, "*.css", SearchOption.AllDirectories))
        {
            string relative = RepositorySource.RelativePath(root, file);

            if (ExcludedSegments.Any(segment => relative.Split('/').Contains(segment)))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static IEnumerable<string> IndexHtmlFiles(DirectoryInfo root)
    {
        string appsPath = Path.Combine(root.FullName, AppsTree);

        foreach (string appDirectory in Directory.EnumerateDirectories(appsPath))
        {
            string indexHtml = Path.Combine(appDirectory, "index.html");
            if (File.Exists(indexHtml))
            {
                yield return RepositorySource.RelativePath(root, indexHtml);
            }
        }
    }
}
