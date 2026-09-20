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
    /// An <b>invocation</b> of <c>wget</c>/<c>curl</c> — followed by a flag, a
    /// URL, or a shell variable reference (<c>$VAR</c> / <c>${VAR}</c>, optionally
    /// quoted) — not merely the word appearing anywhere on the logical
    /// instruction. A bare <c>\b(wget|curl)\b</c> also matches <c>apt-get install
    /// … wget …</c> (line 19: installing the tool as a package, not calling it),
    /// which is not a download this guard should judge; the declared expected red
    /// (spec 195, F3 on line 23 only) is the discriminator that caught this. The
    /// variable branch exists because <c>RUN wget "$TARBALL_URL"</c> — hoisting
    /// the URL into an <c>ARG</c> alongside <c>MOSQUITTO_SHA256</c> is a plausible
    /// next edit — would otherwise carry neither a flag nor a literal
    /// <c>http(s)://</c> right after the command and escape detection entirely.
    /// </summary>
    private static readonly Regex RemoteFetch = new(
        @"(?<![\w.-])(wget|curl)\b(?=\s+(-|[""']?(https?://|\$\{?\w)))",
        RegexOptions.Compiled);

    /// <summary>
    /// Docker's own remote-fetch instruction — <c>ADD &lt;url&gt; &lt;dest&gt;</c>
    /// (optionally with flags such as <c>--chown=</c> or <c>--checksum=</c> before
    /// the URL). Matched wherever an <c>ADD</c> instruction names an
    /// <c>http(s)://</c> URL anywhere on the logical line: this guard only needs
    /// to know the instruction fetches something remote, not which token Docker
    /// itself would resolve as the source.
    /// </summary>
    private static readonly Regex AddRemoteFetch = new(
        @"^\s*ADD\b.*\bhttps?://",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChecksumCheck = new(@"sha256sum\s+-c", RegexOptions.Compiled);

    /// <summary>
    /// Docker's native <c>ADD --checksum=sha256:&lt;64 hex&gt;</c> flag —
    /// satisfies verification for an <c>ADD</c>-based fetch the same way a
    /// subsequent <c>sha256sum -c</c> does for <c>wget</c>/<c>curl</c>.
    /// </summary>
    private static readonly Regex AddChecksumFlag = new(
        @"--checksum=sha256:[0-9a-f]{64}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CommentLine = new(@"^\s*#", RegexOptions.Compiled);

    /// <summary>
    /// The population is found by <b>two</b> globs, unioned: <c>Dockerfile*</c>
    /// (which — .NET's <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
    /// treats it as "starts with Dockerfile" — also matches this very class's own
    /// file name, <c>DockerfileUpstreamPinTests.cs</c>, whose source then contains
    /// the literal words <c>wget</c>/<c>curl</c> in its own regex patterns and doc
    /// comments, inflating the fetch count) and <c>*.Dockerfile</c> (the
    /// <c>&lt;name&gt;.Dockerfile</c> convention Docker tooling's <c>-f</c> flag
    /// and a compose file's <c>dockerfile:</c> entry both recognise, e.g.
    /// <c>api.Dockerfile</c>). This second, exact filter keeps only
    /// <c>Dockerfile</c> itself, <c>Dockerfile.&lt;suffix&gt;</c> (e.g.
    /// <c>Dockerfile.dev</c>), or <c>&lt;name&gt;.Dockerfile</c> — the shapes
    /// Docker tooling itself recognises — and excludes everything else either
    /// coarse glob picked up, this class's own source file included.
    /// </summary>
    private static readonly Regex DockerfileName = new(
        @"^(Dockerfile(\.[A-Za-z0-9_.-]+)?|[A-Za-z0-9_.-]+\.Dockerfile)$",
        RegexOptions.Compiled);

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
    ///
    /// <para>
    /// Grouped by <b>repository</b> (everything before the first <c>:</c> in the
    /// reference, e.g. <c>debian</c>), not by image+tag. Grouping by image+tag
    /// misses a half-applied bump: editing the builder stage's <c>FROM</c> to
    /// <c>debian:trixie-slim@&lt;newdigest&gt;</c> while the runtime stage stays on
    /// <c>debian:bookworm-slim@&lt;olddigest&gt;</c> would put each in its own
    /// single-member group, trivially "agreeing" with itself — exactly the
    /// scenario this fact exists to catch. See
    /// <see cref="A_half_applied_Debian_bump_disagrees_on_repository_even_though_the_tags_differ"/>
    /// for the counterfactual proof.
    /// </para>
    /// </summary>
    [Fact]
    public void Two_stages_that_build_from_one_image_name_one_digest()
    {
        Disagreement[] disagreements = PinnedImageDigestDisagreements();

        disagreements.ShouldBeEmpty(DisagreementExplanation(disagreements));
    }

    /// <summary>
    /// Synthetic-input counterfactual for F2 (reviewer-supplied, phase-6 pass on
    /// #2296): grouping by image+tag would put <c>debian:trixie-slim</c> and
    /// <c>debian:bookworm-slim</c> in two separate, individually-agreeing groups
    /// and miss the disagreement entirely. Grouping by repository must still flag
    /// it, whichever way the two stages diverge (different tag here; different
    /// digest, or both, are the same defect).
    /// </summary>
    [Fact]
    public void A_half_applied_Debian_bump_disagrees_on_repository_even_though_the_tags_differ()
    {
        UpstreamReference[] synthetic =
        [
            new("src/AppHost/mosquitto/Dockerfile", 28, "debian:trixie-slim", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            new("src/AppHost/mosquitto/Dockerfile", 56, "debian:bookworm-slim", "3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251"),
        ];

        Disagreement[] disagreements = PinnedImageDigestDisagreements(synthetic);

        disagreements.Length.ShouldBe(1);
        disagreements[0].Repository.ShouldBe("debian");
        disagreements[0].References.Length.ShouldBe(2);
    }

    [Fact]
    public void Every_archive_a_dockerfile_downloads_is_verified_against_a_checksum()
    {
        FetchInstruction[] fetches = Fetches();

        fetches.Length.ShouldBeGreaterThan(
            0,
            "no remote fetch (wget/curl/ADD) was found in any Dockerfile under the repository, but "
            + "src/AppHost/mosquitto/Dockerfile has one — the scan is broken, not the code.");

        FetchInstruction[] unverified = [.. fetches.Where(fetch => !fetch.HasChecksum)];

        unverified.ShouldBeEmpty(UnverifiedExplanation(unverified));
    }

    /// <summary>
    /// Synthetic-input proof for the <c>ADD</c> branch (reviewer-supplied, phase-6
    /// pass on #2296): Docker's own remote-fetch instruction is entirely different
    /// syntax from <c>wget</c>/<c>curl</c> and was previously invisible to this
    /// guard. No live case exists in this repository's Dockerfile — this is
    /// precision hardening for a form nothing currently uses.
    /// </summary>
    [Fact]
    public void An_ADD_instruction_fetching_a_url_is_detected_and_its_native_checksum_flag_satisfies_it()
    {
        LogicalInstruction unverifiedInstruction = new(
            "Dockerfile",
            1,
            "ADD https://example.com/archive.tar.gz /tmp/archive.tar.gz");
        LogicalInstruction checksummed = new(
            "Dockerfile",
            2,
            "ADD --checksum=sha256:d665fe7d0032881b1371a47f34169ee4edab67903b2cd2b4c083822823f4448a "
            + "https://example.com/archive.tar.gz /tmp/archive.tar.gz");
        LogicalInstruction local = new("Dockerfile", 3, "ADD ./local-file.txt /tmp/local-file.txt");

        FetchInstruction? uncheckedFetch = ClassifyFetch(unverifiedInstruction);
        FetchInstruction? checksummedFetch = ClassifyFetch(checksummed);

        uncheckedFetch.ShouldNotBeNull();
        uncheckedFetch.HasChecksum.ShouldBeFalse();
        checksummedFetch.ShouldNotBeNull();
        checksummedFetch.HasChecksum.ShouldBeTrue();
        ClassifyFetch(local).ShouldBeNull();
    }

    /// <summary>
    /// Synthetic-input proof that a URL held in a variable is still detected
    /// (reviewer-supplied, phase-6 pass on #2296): the previous lookahead required
    /// a flag or a literal <c>http(s)://</c> right after <c>wget</c>/<c>curl</c>,
    /// so <c>RUN wget "$TARBALL_URL"</c> was invisible — concerning because this
    /// PR just introduced <c>ARG MOSQUITTO_SHA256</c> alongside the existing
    /// <c>ARG MOSQUITTO_VERSION</c>, and hoisting the URL into a similar
    /// <c>ARG</c> is a plausible next edit that would otherwise escape detection
    /// silently.
    /// </summary>
    [Fact]
    public void A_fetch_naming_its_url_through_a_shell_variable_is_still_detected()
    {
        ClassifyFetch(new LogicalInstruction("Dockerfile", 1, "RUN wget \"$TARBALL_URL\"")).ShouldNotBeNull();
        ClassifyFetch(new LogicalInstruction("Dockerfile", 2, "RUN wget \"${MOSQUITTO_URL}\"")).ShouldNotBeNull();
        ClassifyFetch(new LogicalInstruction(
            "Dockerfile",
            3,
            "RUN apt-get install -y --no-install-recommends build-essential wget ca-certificates"))
            .ShouldBeNull();
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

    /// <summary>
    /// End-to-end, filesystem-level proof for the widened population glob
    /// (reviewer-supplied, phase-6 pass on #2296): a live <c>&lt;name&gt;.Dockerfile</c>
    /// probe file, created and deleted within this test, must be picked up by
    /// <see cref="DockerfileFiles"/> — and this class's own source file must
    /// still not be, confirming the wider glob did not reintroduce the
    /// self-match bug <see cref="DockerfileName"/>'s doc comment describes.
    /// No such file exists in the repository today — that is exactly why this
    /// probe creates one rather than relying on real content.
    /// </summary>
    [Fact]
    public void The_file_discovery_glob_also_matches_the_name_dot_Dockerfile_convention()
    {
        DirectoryInfo root = RepositoryRoot();
        string probeDirectory = Path.Combine(root.FullName, $".dockerfile-glob-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        string probeFile = Path.Combine(probeDirectory, "api.Dockerfile");

        try
        {
            File.WriteAllText(probeFile, "FROM debian:bookworm-slim" + Environment.NewLine);

            string[] files = DockerfileFiles();

            files.ShouldContain(file => file.EndsWith("api.Dockerfile", StringComparison.Ordinal));
            files.ShouldNotContain(file => file.EndsWith("DockerfileUpstreamPinTests.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
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
        $"{disagreements.Length} image repositor{(disagreements.Length == 1 ? "y" : "ies")} are built "
        + "from more than one tag or digest within this repository's Dockerfile(s):"
        + Environment.NewLine
        + string.Join(
            Environment.NewLine,
            disagreements.Select(disagreement =>
                $"  {disagreement.Repository}:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    disagreement.References.Select(reference =>
                        $"    {reference.File}:{reference.Line} → {reference.ImageAndTag}@sha256:{reference.Digest}"))))
        + Environment.NewLine
        + "Every stage that builds from this repository must resolve the same tag and digest — in "
        + "particular, the runtime stage must carry the same glibc the builder stage linked the broker "
        + "against, or the broker fails to start. Pin every FROM line naming this image to the same "
        + "tag and digest.";

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

    private static Disagreement[] PinnedImageDigestDisagreements() =>
        PinnedImageDigestDisagreements(UpstreamReferences());

    /// <summary>
    /// Grouped by <b>repository</b> — everything before the first <c>:</c> in
    /// <see cref="UpstreamReference.ImageAndTag"/> — not by image+tag. Two
    /// references to the same repository disagree if either the tag or the
    /// digest differs; grouping by image+tag would put each differently-tagged
    /// reference in its own single-member group, which trivially "agrees" with
    /// itself and misses a half-applied bump.
    /// </summary>
    private static Disagreement[] PinnedImageDigestDisagreements(UpstreamReference[] references)
    {
        UpstreamReference[] pinned = [.. references.Where(reference => reference.Digest is not null)];

        return
        [
            .. pinned
                .GroupBy(reference => RepositoryPortion(reference.ImageAndTag), StringComparer.Ordinal)
                .Where(group => group
                    .Select(reference => (reference.ImageAndTag, reference.Digest))
                    .Distinct()
                    .Count() > 1)
                .Select(group => new Disagreement(group.Key, [.. group])),
        ];
    }

    private static string RepositoryPortion(string imageAndTag)
    {
        int colonIndex = imageAndTag.IndexOf(':');
        return colonIndex < 0 ? imageAndTag : imageAndTag[..colonIndex];
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
                FetchInstruction? fetch = ClassifyFetch(instruction);
                if (fetch is not null)
                {
                    fetches.Add(fetch);
                }
            }
        }

        return [.. fetches];
    }

    /// <summary>
    /// A single logical instruction, judged in isolation — no file/directory
    /// context needed, which is what lets the synthetic-input tests exercise this
    /// directly instead of writing a throwaway Dockerfile to disk. Recognises
    /// either <c>wget</c>/<c>curl</c> (<see cref="RemoteFetch"/>) or Docker's own
    /// <c>ADD &lt;url&gt;</c> (<see cref="AddRemoteFetch"/>); verification is
    /// satisfied by either a subsequent <c>sha256sum -c</c>
    /// (<see cref="ChecksumCheck"/>) or <c>ADD</c>'s native
    /// <c>--checksum=sha256:…</c> flag (<see cref="AddChecksumFlag"/>) — whichever
    /// applies to the fetch style actually used.
    /// </summary>
    private static FetchInstruction? ClassifyFetch(LogicalInstruction instruction)
    {
        bool isFetch = RemoteFetch.IsMatch(instruction.Text) || AddRemoteFetch.IsMatch(instruction.Text);
        if (!isFetch)
        {
            return null;
        }

        bool hasChecksum = ChecksumCheck.IsMatch(instruction.Text) || AddChecksumFlag.IsMatch(instruction.Text);

        return new FetchInstruction(instruction.File, instruction.Line, ExtractUrl(instruction.Text), hasChecksum);
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
                .Concat(Directory.EnumerateFiles(root.FullName, "*.Dockerfile", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// One repository (e.g. <c>debian</c>) that at least two pinned references
    /// disagree on — by tag, by digest, or both.
    /// </summary>
    private sealed record Disagreement(string Repository, UpstreamReference[] References);
}
