using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 021 FR-007. A repository that calls <c>SaveChangesAsync</c> directly
/// commits its rows and leaves the announcements behind — which is the defect
/// this feature closed, and the one a repository added later reintroduces by
/// default, because that is what every EF tutorial shows.
///
/// <para>
/// The guarantee is a property of a call site, not of a type, so nothing in the
/// type system holds it. This test is what holds it. It is deliberately a rule
/// with no exemption list: the one repository that announces nothing
/// (<c>DeadLetterRepository</c>) commits through the same seam anyway, precisely
/// so that this can be absolute. An exemption list rots — the next repository
/// added by copying an exempt one inherits the exemption without the reason.
/// </para>
/// </summary>
public class OutboxCommitTests
{
    private static readonly string[] PersistenceAssemblies =
    [
        "SmartSentinelEye.CameraCatalog.Infrastructure",
        "SmartSentinelEye.StreamDistribution.Infrastructure",
        "SmartSentinelEye.LayoutComposition.Infrastructure",
        "SmartSentinelEye.SystemVariables.Infrastructure",
        "SmartSentinelEye.EventIngestion.Infrastructure",
        "SmartSentinelEye.OverlayDesigner.Infrastructure",
        "SmartSentinelEye.Automation.Infrastructure",
        "SmartSentinelEye.Identity.Infrastructure",
        "SmartSentinelEye.AuditObservability.Infrastructure",
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void No_repository_commits_without_its_announcements(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        List<string> offenders = [.. assembly.GetTypes()
            .Where(type => type.Namespace?.Contains(".Persistence", StringComparison.Ordinal) == true)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Where(CallsSaveChangesDirectly)
            .Select(type => type.FullName ?? type.Name)];

        offenders.ShouldBeEmpty(
            $"{string.Join(", ", offenders)} calls SaveChangesAsync directly. Commit through "
            + "ITransactionalCommit instead, so the rows and the integration events they "
            + "announce land in one transaction (spec 021 FR-001). Committing directly is "
            + "silent: the write succeeds, the caller is told the truth, and the "
            + "announcement is never made.");
    }

    public static TheoryData<string> Assemblies()
    {
        TheoryData<string> data = [];
        foreach (string assembly in PersistenceAssemblies)
        {
            data.Add(assembly);
        }

        return data;
    }

    /// <summary>
    /// Reads the IL rather than the source, because the call is what matters and
    /// a comment saying "we use the outbox" is not a constraint. Any reference
    /// to <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> from a
    /// repository body is an offence — including one buried in a helper, which
    /// is how it would come back.
    ///
    /// <para>
    /// <b>The nested types are the point.</b> Every repository's <c>SaveAsync</c>
    /// is <c>async</c>, so its body is compiled into a state machine
    /// (<c>CameraRepository+&lt;SaveAsync&gt;d__7.MoveNext</c>) and the declared
    /// method holds nothing but <c>AsyncTaskMethodBuilder</c> plumbing. Scanning
    /// only declared methods — which is what the first version of this rule did —
    /// therefore catches the one shape nobody writes and misses every
    /// <c>await dbContext.SaveChangesAsync(ct)</c> in the repository.
    /// </para>
    /// </summary>
    private static bool CallsSaveChangesDirectly(Type type) =>
        BodiesOf(type).Any(body => ReferencesSaveChanges(body, type.Module));

    private static IEnumerable<MethodBody> BodiesOf(Type type)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        IEnumerable<Type> types = [type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)];

        return types
            .SelectMany(candidate => candidate.GetMethods(Declared).Cast<MethodBase>()
                .Concat(candidate.GetConstructors(Declared)))
            .Select(method => method.GetMethodBody())
            .Where(body => body is not null)
            .Select(body => body!);
    }

    private static bool ReferencesSaveChanges(MethodBody body, Module module)
    {
        byte[] il = body.GetILAsByteArray() ?? [];

        // 0x28 call, 0x6F callvirt — the two ways SaveChangesAsync is reached.
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, i + 1);
            try
            {
                MethodBase? called = module.ResolveMethod(token);
                // IsAssignableFrom, not IsSubclassOf: SaveChangesAsync is declared
                // on DbContext itself, so a subclass check excludes the only
                // declaring type it ever has — which is how the first version of
                // this rule passed against a repository deliberately broken to
                // fail it.
                if (called?.Name == nameof(DbContext.SaveChangesAsync)
                    && typeof(DbContext).IsAssignableFrom(called.DeclaringType))
                {
                    return true;
                }
            }
            catch (Exception resolution) when (
                resolution is ArgumentException
                or FileNotFoundException
                or BadImageFormatException
                or MissingMethodException)
            {
                // Not a method token at this offset — the byte was operand data
                // rather than an opcode. Scanning IL without decoding it fully
                // means this happens, and a bogus token can fail to resolve in
                // several ways, not only as an ArgumentException. None of them
                // is a finding.
            }
        }

        return false;
    }
}
