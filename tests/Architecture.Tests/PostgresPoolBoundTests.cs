using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Issue 1962 / ADR-0125. A context that reads its database connection string
/// straight from configuration gets Npgsql's default cap of 100 connections per
/// pool, and nine of those against one server is a budget nobody can honour.
///
/// <para>
/// Like <c>OutboxCommitTests</c>, the guarantee is a property of a call site
/// rather than of a type, and the default is the wrong one — reading
/// configuration directly is what every example shows. So this is what holds it,
/// and deliberately with no exemption list.
/// </para>
///
/// <para>
/// <b>The failure it prevents does not name its cause.</b> When the budget runs
/// out, the service refused a connection is whichever one asks next, not the one
/// that took them: an unbounded audit pool produced
/// <c>53300: sorry, too many clients already</c> on <c>system-variables</c>'
/// write path, with a stack trace pointing only at that context.
/// </para>
/// </summary>
public class PostgresPoolBoundTests
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
    public void No_context_reads_its_database_connection_string_unbounded(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        List<string> offenders = [.. assembly.GetTypes()
            .SelectMany(type => UnboundedDatabaseReads(type).Select(name => $"{type.Name} -> \"{name}\""))];

        offenders.ShouldBeEmpty(
            $"{string.Join(", ", offenders)} reads a database connection string straight from "
            + "configuration, so its pool keeps Npgsql's default cap of 100. Read it through "
            + "PostgresConnectionBudget.GetBoundedPostgresConnectionString instead (ADR-0125). "
            + "An unbounded pool is silent until the shared server runs out, and then the "
            + "service that fails is a different one.");
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
    /// Reads the IL, because a comment saying "bounded" is not a bound.
    ///
    /// <para>
    /// Scoped to connection names ending <c>-db</c>, which is the Aspire resource
    /// convention for a Postgres database and distinguishes them from the
    /// <c>keycloak</c>, <c>rabbitmq</c> and <c>mediamtx-*</c> strings these same
    /// modules legitimately read. The name reaches the call site as a literal:
    /// <c>DatabaseConnectionName</c> is a <c>const</c>, so the compiler inlines
    /// it to an <c>ldstr</c> immediately before the call.
    /// </para>
    /// </summary>
    private static IEnumerable<string> UnboundedDatabaseReads(Type type)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        IEnumerable<Type> candidates = [type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)];

        foreach (Type candidate in candidates)
        {
            foreach (MethodBase method in candidate.GetMethods(Declared).Cast<MethodBase>()
                         .Concat(candidate.GetConstructors(Declared)))
            {
                MethodBody? body = method.GetMethodBody();
                if (body is null)
                {
                    continue;
                }

                foreach (string name in DatabaseNamesReadDirectly(body, candidate.Module))
                {
                    yield return name;
                }
            }
        }
    }

    private static IEnumerable<string> DatabaseNamesReadDirectly(MethodBody body, Module module)
    {
        byte[] il = body.GetILAsByteArray() ?? [];

        // ldstr (0x72) + token, then call (0x28) or callvirt (0x6F) + token.
        for (int i = 0; i + 9 < il.Length; i++)
        {
            if (il[i] != 0x72 || il[i + 5] is not (0x28 or 0x6F))
            {
                continue;
            }

            string? literal = Resolve(() => module.ResolveString(BitConverter.ToInt32(il, i + 1)));
            if (literal is null || !literal.EndsWith("-db", StringComparison.Ordinal))
            {
                continue;
            }

            MethodBase? called = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, i + 6)));
            if (called?.Name == nameof(ConfigurationExtensions.GetConnectionString)
                && called.DeclaringType == typeof(ConfigurationExtensions))
            {
                yield return literal;
            }
        }
    }

    /// <summary>
    /// A byte sequence that looks like a token need not be one — the scan walks
    /// every offset, not just instruction boundaries, so a mis-resolve is
    /// expected rather than exceptional and means "not the call we are looking
    /// for".
    /// </summary>
    private static T? Resolve<T>(Func<T?> resolve)
        where T : class
    {
        try
        {
            return resolve();
        }
        catch (Exception resolution) when (
            resolution is ArgumentException or BadImageFormatException or MissingMemberException)
        {
            return null;
        }
    }
}
