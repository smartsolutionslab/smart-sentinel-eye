using System.Text;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards that a Dockerfile does not trust a floating upstream (issue #2296,
/// spec 195) — the surface <c>ContainerImagePinTests</c> (issue #2103, spec
/// 113) and <c>AppHostContainerImagePinTests</c> (issue #2270, spec 187) both
/// leave untouched. Neither of those two can see this: the first reads
/// <c>AppHost.cs</c>/<c>scripts/</c> as text, whose grammar has nothing to do
/// with <c>FROM</c>/<c>RUN</c>; the second reads the composed
/// <see cref="ContainerImageAnnotation"/>, and a <c>DockerfileBuildAnnotation</c>
/// resource such as <c>mosquitto</c> carries a content-addressed local tag
/// instead — no <c>FROM</c> line feeds into it at all. This class is the
/// authority for a Dockerfile's own upstreams; the other two remain the
/// authority for what <c>AppHost.cs</c>/<c>scripts/</c> write and what the
/// composed model resolves, respectively.
///
/// <para>
/// Reads the tree from disk, exactly as <c>ContainerImagePinTests</c> does and
/// for the same reason (ADR-0103): build configuration leaves no trace in IL,
/// and composing a container to read a text file would be absurd.
/// </para>
///
/// <para>
/// The population is found by <b>glob</b> (<c>Dockerfile*</c> under the
/// repository root), never by a filename constant — a name list narrows
/// silently the day a second Dockerfile appears; spec 187 rejected the same
/// shortcut for the same reason.
/// </para>
///
/// <para>
/// <b>No fact here names a digest, a version, an image or a checksum.</b> This
/// bans a category, never a value — bumping Debian or Go must not require
/// editing the guard that governs Debian or Go.
/// </para>
/// </summary>
public class DockerfileUpstreamPinTests
{
    private static readonly string[] ExcludedSegments = ["node_modules", "obj", "bin"];

    /// <summary>
    /// <c>FROM &lt;ref&gt; [AS &lt;stage&gt;]</c>, case-insensitive — Dockerfile
    /// accepts either spelling. Applied to a comment-stripped, continuation-folded
    /// logical instruction, so it never sees the word "FROM" inside the header
    /// paragraph at line 3.
    /// </summary>
    private static readonly Regex FromInstruction = new(
        @"^\s*FROM\s+(?<ref>\S+)(\s+AS\s+(?<stage>\S+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A trailing <c>@sha256:&lt;64 hex&gt;</c>, anchored at the end of the
    /// reference — <c>@sha256:abc</c> is not a digest.
    /// </summary>
    private static readonly Regex DigestSuffix = new(
        @"@sha256:(?<digest>[0-9a-f]{64})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// An <b>invocation</b> of <c>wget</c>/<c>curl</c> — followed by a flag or a
    /// URL — not merely the word appearing anywhere on the logical instruction.
    /// A bare <c>\b(wget|curl)\b</c> also matches <c>apt-get install … wget …</c>
    /// (line 19: installing the tool as a package, not calling it), which is not
    /// a download this guard should judge; the declared expected red (spec 195,
    /// F3 on line 23 only) is the discriminator that caught this.
    /// </summary>
    private static readonly Regex RemoteFetch = new(
        @"(?<![\w.-])(wget|curl)\b(?=\s+(-|[""']?https?://))",
        RegexOptions.Compiled);

    private static readonly Regex ChecksumCheck = new(@"sha256sum\s+-c", RegexOptions.Compiled);

    private static readonly Regex CommentLine = new(@"^\s*#", RegexOptions.Compiled);

    /// <summary>
    /// The glob that finds the population, <c>Dockerfile*</c>, is deliberately
    /// coarse — .NET's <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
    /// treats it as "starts with Dockerfile", which also matches this very
    /// class's own file name, <c>DockerfileUpstreamPinTests.cs</c> — whose source
    /// then contains the literal words <c>wget</c>/<c>curl</c> in its own regex
    /// patterns and doc comments, inflating the fetch count. This second, exact
    /// filter keeps only <c>Dockerfile</c> itself or <c>Dockerfile.&lt;suffix&gt;</c>
    /// (e.g. <c>Dockerfile.dev</c>) — the shapes Docker tooling itself recognises
    /// — and excludes everything else the coarse glob picked up.
    /// </summary>
    private static readonly Regex DockerfileName = new(@"^Dockerfile(\.[A-Za-z0-9_.-]+)?$", RegexOptions.Compiled);

    private static readonly Regex UrlLiteral = new(@"https?://\S+", RegexOptions.Compiled);

    [Fact]
    public void Every_base_image_a_dockerfile_builds_from_is_pinned_by_digest()
    {
        UpstreamReference[] references = UpstreamReferences();

        references.Length.ShouldBeGreaterThan(
            0,
            "no FROM reference naming a registry image was found in any Dockerfile under the "
            + "repository — the scan is broken, not the code. A guard that matches nothing passes, "
            + "and a passing guard that checks nothing is indistinguishable from one that holds.");

        UpstreamReference[] floating = [.. references.Where(reference => reference.Digest is null)];

        floating.ShouldBeEmpty(FloatingExplanation(floating));
    }

    /// <summary>
    /// Both <c>debian:bookworm-slim</c> stages must resolve the same bytes —
    /// otherwise a security fix baked into one Debian layer is silently absent
    /// from the other, and (ADR-0100) the runtime stage specifically must carry
    /// the same glibc the builder stage linked the broker against, or Mosquitto
    /// fails to start.
    /// </summary>
    [Fact]
    public void Two_stages_that_build_from_one_image_name_one_digest()
    {
        Disagreement[] disagreements = PinnedImageDigestDisagreements();

        disagreements.ShouldBeEmpty(DisagreementExplanation(disagreements));
    }

    [Fact]
    public void Every_archive_a_dockerfile_downloads_is_verified_against_a_checksum()
    {
        FetchInstruction[] fetches = Fetches();

        fetches.Length.ShouldBeGreaterThan(
            0,
            "no remote fetch (wget/curl) was found in any Dockerfile under the repository, but "
            + "src/AppHost/mosquitto/Dockerfile has one — the scan is broken, not the code.");

        FetchInstruction[] unverified = [.. fetches.Where(fetch => !fetch.HasChecksum)];

        unverified.ShouldBeEmpty(UnverifiedExplanation(unverified));
    }

    /// <summary>
    /// The positive control this class rests on (mirrors
    /// <c>ContainerImagePinTests</c>' and <c>AppHostContainerImagePinTests</c>'
    /// own). Floors, never equalities — spec 187's rule, so removing a stage is
    /// a conversation and not a red build.
    /// </summary>
    [Fact]
    public void The_scan_reads_the_dockerfiles_this_repository_actually_has()
    {
        string[] files = DockerfileFiles();

        files.Length.ShouldBeGreaterThanOrEqualTo(
            1,
            "expected at least one Dockerfile under the repository root and found none — the scan "
            + "is broken, not the code.");

        UpstreamReferences().Length.ShouldBeGreaterThanOrEqualTo(
            3,
            "expected at least three judged FROM references (the two debian:bookworm-slim stages "
            + "and the golang:1.23-bookworm stage in src/AppHost/mosquitto/Dockerfile) and found "
            + $"{UpstreamReferences().Length}. Either a stage was removed (update this number and say "
            + "why) or the scan no longer matches how FROM is written.");

        Fetches().Length.ShouldBeGreaterThanOrEqualTo(
            1,
            "expected at least one judged remote fetch (the Mosquitto tarball wget) and found "
            + $"{Fetches().Length}. Either the fetch was removed (update this number and say why) or "
            + "the scan no longer matches how it is written.");
    }

    private static string FloatingExplanation(UpstreamReference[] floating) =>
        $"{floating.Length} base image reference(s) in this repository's Dockerfile(s) have no digest:"
        + Environment.NewLine
        + string.Join(
            Environment.NewLine,
            floating.Select(reference => $"  {reference.File}:{reference.Line} → {reference.ImageAndTag}"))
        + Environment.NewLine
        + "A floating base image changes what this build produces on the next pull, on whichever "
        + "machine pulls first, with no diff and no PR (#2265, #2296). Resolve it with `docker "
        + "buildx imagetools inspect <image:tag>` and write `image:tag@sha256:…` — keep the tag, it "
        + "is what tells a reader which release the digest is.";

    private static string DisagreementExplanation(Disagreement[] disagreements) =>
        $"{disagreements.Length} image(s) are built from more than one digest within this repository's "
        + "Dockerfile(s):"
        + Environment.NewLine
        + string.Join(
            Environment.NewLine,
            disagreements.Select(disagreement =>
                $"  {disagreement.ImageAndTag}:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    disagreement.References.Select(reference => $"    {reference.File}:{reference.Line} → @sha256:{reference.Digest}"))))
        + Environment.NewLine
        + "Every stage that names the same image must resolve the same bytes — in particular, the "
        + "runtime stage must carry the same glibc the builder stage linked the broker against, or "
        + "the broker fails to start. Pin every FROM line naming this image to the same digest.";

    private static string UnverifiedExplanation(FetchInstruction[] unverified) =>
        $"{unverified.Length} download(s) in this repository's Dockerfile(s) are never checked "
        + "against a checksum:"
        + Environment.NewLine
        + string.Join(
            Environment.NewLine,
            unverified.Select(fetch => $"  {fetch.File}:{fetch.Line} downloads {fetch.Url}"))
        + Environment.NewLine
        + "The build trusts a third-party host's bytes with nothing to compare them against. Add "
        + "`echo \"<sha256>  <file>\" | sha256sum -c -` to the same RUN.";

    private static Disagreement[] PinnedImageDigestDisagreements()
    {
        UpstreamReference[] pinned = [.. UpstreamReferences().Where(reference => reference.Digest is not null)];

        return
        [
            .. pinned
                .GroupBy(reference => reference.ImageAndTag, StringComparer.Ordinal)
                .Where(group => group.Select(reference => reference.Digest).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(group => new Disagreement(group.Key, [.. group])),
        ];
    }

    private static UpstreamReference[] UpstreamReferences()
    {
        List<UpstreamReference> references = [];

        foreach (string file in DockerfileFiles())
        {
            HashSet<string> declaredStages = new(StringComparer.OrdinalIgnoreCase);

            foreach (LogicalInstruction instruction in LogicalInstructions(file))
            {
                Match match = FromInstruction.Match(instruction.Text);
                if (!match.Success)
                {
                    continue;
                }

                string reference = match.Groups["ref"].Value;
                string? stage = match.Groups["stage"].Success ? match.Groups["stage"].Value : null;

                if (!declaredStages.Contains(reference))
                {
                    Match digestMatch = DigestSuffix.Match(reference);
                    string imageAndTag = digestMatch.Success ? reference[..digestMatch.Index] : reference;
                    string? digest = digestMatch.Success ? digestMatch.Groups["digest"].Value : null;

                    references.Add(new UpstreamReference(file, instruction.Line, imageAndTag, digest));
                }

                if (stage is not null)
                {
                    declaredStages.Add(stage);
                }
            }
        }

        return [.. references];
    }

    private static FetchInstruction[] Fetches()
    {
        List<FetchInstruction> fetches = [];

        foreach (string file in DockerfileFiles())
        {
            foreach (LogicalInstruction instruction in LogicalInstructions(file))
            {
                if (!RemoteFetch.IsMatch(instruction.Text))
                {
                    continue;
                }

                fetches.Add(new FetchInstruction(
                    file,
                    instruction.Line,
                    ExtractUrl(instruction.Text),
                    ChecksumCheck.IsMatch(instruction.Text)));
            }
        }

        return [.. fetches];
    }

    private static string ExtractUrl(string text)
    {
        Match match = UrlLiteral.Match(text);
        return match.Success ? match.Value.TrimEnd('"', '\\') : "(no URL found)";
    }

    /// <summary>
    /// Comments stripped first (a full-line <c>#</c>, so the header paragraph at
    /// line 3 cannot trip the scan). Backslash line continuations are then
    /// folded into one logical instruction, remembering the physical line the
    /// instruction started on — a regex applied per physical line would report a
    /// fetch with no checksum for a <c>RUN</c> that has one, sitting on the next
    /// physical line.
    /// </summary>
    private static LogicalInstruction[] LogicalInstructions(string relativeFile)
    {
        string[] rawLines = ReadLines(relativeFile);
        List<LogicalInstruction> instructions = [];

        int index = 0;
        while (index < rawLines.Length)
        {
            string line = rawLines[index];

            if (CommentLine.IsMatch(line) || string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            int startLine = index + 1;
            StringBuilder text = new(line.TrimEnd());
            index++;

            while (text.ToString().TrimEnd().EndsWith('\\') && index < rawLines.Length)
            {
                string folded = text.ToString().TrimEnd();
                text.Clear();
                text.Append(folded, 0, folded.Length - 1);
                text.Append(' ').Append(rawLines[index].Trim());
                index++;
            }

            instructions.Add(new LogicalInstruction(relativeFile, startLine, text.ToString()));
        }

        return [.. instructions];
    }

    private static string[] DockerfileFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return
        [
            .. Directory
                .EnumerateFiles(root.FullName, "Dockerfile*", SearchOption.AllDirectories)
                .Where(file => DockerfileName.IsMatch(Path.GetFileName(file)))
                .Where(file => !file
                    .Split(Path.DirectorySeparatorChar)
                    .Any(segment => ExcludedSegments.Contains(segment, StringComparer.Ordinal)))
                .Select(file => Relative(root, file))
                .OrderBy(file => file, StringComparer.Ordinal),
        ];
    }

    private static string[] ReadLines(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllLines(path);
    }

    /// <summary>
    /// Reported with <c>/</c> throughout. <see cref="Path.GetRelativePath(string, string)"/>
    /// returns the platform separator, so a backslash in an expected string is
    /// green on Windows and red on Linux CI — this repository has been bitten by
    /// exactly that.
    /// </summary>
    private static string Relative(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// One instruction with its backslash continuations folded in, and the
    /// physical line it started on.
    /// </summary>
    private sealed record LogicalInstruction(string File, int Line, string Text);

    /// <summary>
    /// One non-stage <c>FROM</c> reference. <c>ImageAndTag</c> excludes any
    /// digest suffix, so two references to the same image can be grouped
    /// regardless of whether either is pinned yet.
    /// </summary>
    private sealed record UpstreamReference(string File, int Line, string ImageAndTag, string? Digest);

    private sealed record FetchInstruction(string File, int Line, string Url, bool HasChecksum);

    private sealed record Disagreement(string ImageAndTag, UpstreamReference[] References);
}
