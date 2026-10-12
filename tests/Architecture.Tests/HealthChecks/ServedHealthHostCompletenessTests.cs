namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), FR-007. <see cref="ServedHealthHosts"/> is a hand-written
/// list, and a hand-written list can go stale the day a new host starts
/// calling <c>app.MapDefaultEndpoints()</c> — this is the guard that it does
/// not. It is a <b>coverage</b> check only: it never reads whether a host's
/// own registrations obey ADR-0154 clause 1, only whether every host that
/// serves <c>/health</c> is in the list <see cref="HealthCheckFailureStatusGuardTests"/>
/// composes. Its own bite is proven by counterfactual (dropping an entry from
/// <see cref="ServedHealthHosts"/>, SC in plan §7 step 4), not by a red test —
/// the property it guards already holds today, the same precedent
/// <c>DependencyHealthCheckGuardTests</c>' own doc comment records for its
/// ten-host theory.
///
/// <para>
/// Carries no <see cref="HostCaptureCollection"/> attribute: it is a source
/// scan, it builds no host, and it opens no process-global hosting hook.
/// </para>
/// </summary>
public class ServedHealthHostCompletenessTests
{
    private const string ProgramFileName = "Program.cs";
    private const string MapDefaultEndpointsCall = "MapDefaultEndpoints(";

    /// <summary>
    /// A <c>Program.cs</c> under <c>src/</c> that calls <c>MapDefaultEndpoints</c>,
    /// paired with the single <c>*.csproj</c> in its own directory — the file name
    /// without extension is the assembly name, true of every host under
    /// <c>src/</c> today (spec §1.4, confirmed 2026-10-12: no <c>src</c> project
    /// sets <c>&lt;AssemblyName&gt;</c>).
    /// </summary>
    private sealed record ServedProgramFile(string ProgramPath, string AssemblyName);

    /// <summary>
    /// <b>Every host that maps <c>/health</c> is guarded, and every guarded host
    /// maps <c>/health</c>.</b> Read in both directions, like the registers
    /// <c>EndpointScopeDeclarationTests</c> keeps for the same reason: a one-way
    /// list is an allow-list with better manners, and this would otherwise never
    /// notice an assembly <see cref="ServedHealthHosts"/> lists that no longer
    /// serves <c>/health</c> at all.
    /// </summary>
    [Fact]
    public void Every_host_that_maps_health_is_guarded_and_every_guarded_host_maps_health()
    {
        ServedProgramFile[] served = ServedProgramFiles();
        HashSet<string> servedAssemblyNames = new(
            served.Select(file => file.AssemblyName), StringComparer.Ordinal);
        HashSet<string> guardedAssemblyNames = new(
            ServedHealthHosts.All.Select(host => host.AssemblyName), StringComparer.Ordinal);

        string[] unguarded = served
            .Where(file => !guardedAssemblyNames.Contains(file.AssemblyName))
            .Select(file => $"{file.ProgramPath} ({file.AssemblyName})")
            .ToArray();

        unguarded.ShouldBeEmpty(
            "these Program.cs files call MapDefaultEndpoints and so serve /health, but their assembly is "
            + $"not in ServedHealthHosts: {Environment.NewLine}{string.Join(Environment.NewLine, unguarded)}"
            + $"{Environment.NewLine}HealthCheckFailureStatusGuardTests never composes this host, so ADR-0154 "
            + "clause 1 is unguarded for it. Add it to ServedHealthHosts.");

        string[] stale = ServedHealthHosts.All
            .Where(host => !servedAssemblyNames.Contains(host.AssemblyName))
            .Select(host => $"{host.AssemblyName} ({host.ProjectDirectory})")
            .ToArray();

        stale.ShouldBeEmpty(
            "these entries in ServedHealthHosts no longer correspond to a Program.cs that calls "
            + $"MapDefaultEndpoints: {Environment.NewLine}{string.Join(Environment.NewLine, stale)}"
            + $"{Environment.NewLine}Either the host stopped serving /health and the entry is stale, or it "
            + "moved and the entry now points at the wrong project. Either way, delete or correct it — a "
            + "register read in one direction only is an allow-list.");
    }

    private static ServedProgramFile[] ServedProgramFiles()
    {
        DirectoryInfo root = RepositorySource.Root();
        DirectoryInfo srcRoot = new(Path.Combine(root.FullName, "src"));

        return srcRoot
            .EnumerateFiles(ProgramFileName, SearchOption.AllDirectories)
            .Where(file => !RepositorySource.RelativePath(root, file.FullName).Contains("/obj/", StringComparison.Ordinal)
                && !RepositorySource.RelativePath(root, file.FullName).Contains("/bin/", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file.FullName).Contains(MapDefaultEndpointsCall, StringComparison.Ordinal))
            .Select(file => new ServedProgramFile(
                RepositorySource.RelativePath(root, file.FullName), AssemblyNameOf(file)))
            .OrderBy(file => file.ProgramPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string AssemblyNameOf(FileInfo programFile)
    {
        DirectoryInfo directory = programFile.Directory
            ?? throw new InvalidOperationException($"{programFile.FullName} has no containing directory.");

        FileInfo project = directory.EnumerateFiles("*.csproj").SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{directory.FullName} does not contain exactly one .csproj alongside {ProgramFileName}.");

        return Path.GetFileNameWithoutExtension(project.Name);
    }
}
