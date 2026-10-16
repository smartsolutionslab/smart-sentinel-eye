using System.Reflection;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using static SmartSentinelEye.CameraCatalog.Domain.Camera.RtspUrl;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 389 / issue #2999, FR-008, adversarial (T050). <c>FromStoredCallSiteTests</c> scans source text line by
/// line for <c>RtspUrl.FromStored</c>, so a call written as <c>using static</c> plus a bare <c>FromStored(...)</c>,
/// or with the type and the member on separate lines, passes it (both observed by counterfactual). This guard reads
/// the compiled IL instead, where every spelling is the same call instruction: across every
/// <c>SmartSentinelEye.*</c> production assembly, the only methods that call or take the address of either
/// <c>FromStored</c> belong to the two persistence configurations.
/// </summary>
public class FromStoredCallerIlTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly MethodInfo[] Targets =
    [
        typeof(RtspUrl).GetMethod(nameof(RtspUrl.FromStored))!,
        typeof(StreamSourceUrl).GetMethod(nameof(StreamSourceUrl.FromStored))!,
    ];

    [Fact]
    public void Only_the_two_persistence_configurations_call_FromStored_in_the_compiled_code()
    {
        var callers = ProductionAssemblies().SelectMany(Callers).Distinct().Order(StringComparer.Ordinal).ToList();

        callers.ShouldBe(
        [
            "SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Configurations.CameraConfiguration",
            "SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Configurations.StreamConfiguration",
        ]);
    }

    [Fact]
    public void A_using_static_call_a_split_line_call_and_a_method_group_are_all_found()
    {
        var callers = Callers(typeof(FromStoredCallerIlTests).Assembly).ToList();

        callers.ShouldContain(typeof(FromStoredIlUsingStaticProbe).FullName!);
        callers.ShouldContain(typeof(FromStoredIlSplitLineProbe).FullName!);
        callers.ShouldContain(typeof(FromStoredIlMethodGroupProbe).FullName!);
    }

    private static IEnumerable<Assembly> ProductionAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "SmartSentinelEye.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom);

    private static IEnumerable<string> Callers(Assembly assembly)
    {
        foreach (var type in LoadableTypes(assembly))
        {
            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                if (CallsATarget(method))
                {
                    yield return OutermostType(type).FullName!;
                }
            }
        }
    }

    // A call, callvirt, ldtoken (an expression tree, which is what the converters' read lambdas compile to), ldftn
    // or ldvirtftn operand is a four-byte method token right after the opcode. Every offset is tried rather than
    // decoding the instruction stream: a token that resolves to exactly one of the two targets by coincidence of
    // operand bytes is not a realistic false positive, and a missed call would be.
    private static bool CallsATarget(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            return false;
        }

        for (var offset = 0; offset + 5 <= il.Length; offset++)
        {
            var operandAt = OperandOffset(il, offset);

            if (operandAt >= 0 && IsTarget(method.Module, BitConverter.ToInt32(il, operandAt)))
            {
                return true;
            }
        }

        return false;
    }

    private static int OperandOffset(byte[] il, int offset)
    {
        if (il[offset] is 0x28 or 0x6F or 0xD0)
        {
            return offset + 1;
        }

        var isFunctionPointerLoad = il[offset] == 0xFE && offset + 6 <= il.Length && il[offset + 1] is 0x06 or 0x07;

        return isFunctionPointerLoad ? offset + 2 : -1;
    }

    private static bool IsTarget(Module module, int token)
    {
        var table = (token >> 24) & 0xFF;

        if (table is not (0x06 or 0x0A or 0x2B))
        {
            return false;
        }

        try
        {
            var resolved = module.ResolveMethod(token);

            return resolved is not null
                && Targets.Any(target => target.Name == resolved.Name && target.DeclaringType!.FullName == resolved.DeclaringType?.FullName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}

internal static class FromStoredIlUsingStaticProbe
{
    public static RtspUrl Read(string address) => FromStored(address);
}

internal static class FromStoredIlSplitLineProbe
{
    public static StreamSourceUrl Read(string address) => StreamSourceUrl
        .FromStored(address);
}

internal static class FromStoredIlMethodGroupProbe
{
    public static Func<string, RtspUrl> Reader() => RtspUrl.FromStored;
}
