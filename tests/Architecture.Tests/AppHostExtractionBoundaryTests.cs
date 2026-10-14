namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 354 (#2898): keeps the tokens two existing source guards read inside
/// <c>src/AppHost/AppHost.cs</c> while the rest of that file is split into
/// extension methods in sibling files.
///
/// <para>
/// <see cref="ContainerImagePinTests"/> reads exactly <c>AppHost.cs</c> for
/// container image coordinates, and <see cref="FoundingDecisionRecordTests"/>
/// reads exactly <c>AppHost.cs</c> for any mention of Prometheus (ADR-0118). A
/// declaration moved into a sibling file would leave both guards green while
/// scanning nothing — a guard that passes because its input vanished. They are
/// not edited to follow the move (the issue's owner decision): the move is
/// kept out of their blind spot instead, and this test fails when it is not.
/// </para>
///
/// <para>
/// Non-vacuity is asserted: the scan must have visited at least two sibling
/// files, and <c>AppHost.cs</c> must still carry the tokens, so a broken
/// enumeration or a hollowed-out <c>AppHost.cs</c> cannot pass quietly.
/// </para>
/// </summary>
public class AppHostExtractionBoundaryTests
{
    private const string AppHostFile = "src/AppHost/AppHost.cs";

    private static readonly string[] CaseSensitiveTokens =
    [
        "AddContainer(",
        "AddDockerfile(",
        "WithImage(",
        "WithImageTag(",
        "WithImageRegistry(",
        "bluenviron/mediamtx:",
    ];

    private static readonly string[] GuardedTokensAppHostMustKeep =
    [
        "AddContainer(",
        "WithImageTag(",
        "bluenviron/mediamtx:",
    ];

    [Fact]
    public void No_other_AppHost_source_file_declares_a_container_image_or_mentions_prometheus()
    {
        var root = RepositorySource.Root();
        var siblings = SiblingSources(root);

        siblings.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            $"scanned {siblings.Count} sibling source file(s) under src/AppHost; the scan is "
            + "broken (it should see at least OverridableParameters.cs and StackStatusReport.cs).");

        var offences = Offences(root, siblings);

        offences.ShouldBeEmpty(
            "these AppHost files carry a token ContainerImagePinTests or FoundingDecisionRecordTests "
            + $"reads only in {AppHostFile}; moving it out would leave both guards green while scanning "
            + $"nothing (spec 354): {string.Join("; ", offences)}");
    }

    [Fact]
    public void AppHost_cs_still_carries_the_tokens_the_two_guards_read()
    {
        var source = File.ReadAllText(Path.Combine(RepositorySource.Root().FullName, "src", "AppHost", "AppHost.cs"));

        var missing = GuardedTokensAppHostMustKeep
            .Where(token => !source.Contains(token, StringComparison.Ordinal))
            .ToList();

        missing.ShouldBeEmpty(
            $"{AppHostFile} no longer contains [{string.Join(", ", missing)}]; the scan above would "
            + "then pass on a file that has nothing left to guard.");
    }

    private static List<string> SiblingSources(DirectoryInfo root)
    {
        var appHostDirectory = Path.Combine(root.FullName, "src", "AppHost");

        return Directory
            .EnumerateFiles(appHostDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => RepositorySource.RelativePath(root, file))
            .Where(path => !string.Equals(path, AppHostFile, StringComparison.Ordinal))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> Offences(DirectoryInfo root, List<string> files)
    {
        List<string> offences = [];

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(Path.Combine(root.FullName, file));

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];

                var token = CaseSensitiveTokens.FirstOrDefault(
                    candidate => line.Contains(candidate, StringComparison.Ordinal));

                if (token is null && line.Contains("prometheus", StringComparison.OrdinalIgnoreCase))
                {
                    token = "prometheus";
                }

                if (token is not null)
                {
                    offences.Add($"{file}:{index + 1} contains '{token}'");
                }
            }
        }

        return offences;
    }
}
