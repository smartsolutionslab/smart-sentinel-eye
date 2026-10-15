using System.Reflection;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 377 (#2977) adversarial guard for <see cref="AuditPayloadRedactionCoverageTests"/>. That guard
/// demands a decision for every <c>string</c> or <c>Uri</c> property it can reach, and it reaches nested
/// <c>Shared.Contracts</c> types only when they are classes. A camera URL could therefore bypass it by
/// travelling in a shape it never inspects: an <c>object</c>, a <c>JsonElement</c>, a <c>char[]</c>, or a
/// nested <c>record struct</c>. This test fails as soon as an audited contract carries any property shape
/// outside the set the classification understands, so the coverage guard cannot be outflanked silently.
/// </summary>
public class AuditContractPropertyShapeTests
{
    private static readonly HashSet<Type> Scalars =
    [
        typeof(string),
        typeof(Uri),
        typeof(Guid),
        typeof(DateTimeOffset),
        typeof(bool),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(decimal),
    ];

    [Fact]
    public void Every_property_of_every_audited_contract_has_a_shape_the_url_classification_inspects()
    {
        var contracts = Assembly.Load("SmartSentinelEye.Shared.Contracts");
        var integrationEvent = contracts.GetType("SmartSentinelEye.Shared.Contracts.IIntegrationEvent")!;

        var problems = Unrecognised(
            contracts.GetTypes().Where(type => !type.IsAbstract && !type.IsInterface && integrationEvent.IsAssignableFrom(type)),
            contracts);

        problems.ShouldBeEmpty(
            "An audited contract carries a property shape AuditPayloadRedactionCoverageTests does not inspect, "
            + "so a camera URL in it would be stored unredacted without failing the build:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_shape_check_reports_each_shape_the_classification_cannot_see()
    {
        var problems = Unrecognised([typeof(ProbeV1)], typeof(ProbeV1).Assembly);

        problems.ShouldBe(
        [
            "ProbeV1.Characters: Char[]",
            "ProbeV1.Element: JsonElement",
            "ProbeV1.Loose: Object",
            "ProbeV1.Tile: ProbeTileV1",
        ],
        ignoreOrder: true);
    }

    private static List<string> Unrecognised(IEnumerable<Type> roots, Assembly contracts)
    {
        List<string> problems = [];
        HashSet<Type> seen = [];
        Queue<Type> pending = new(roots);

        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var element = ElementOf(property.PropertyType);
                if (Scalars.Contains(element))
                {
                    continue;
                }

                if (element.Assembly == contracts && element.IsClass && !element.IsAbstract)
                {
                    pending.Enqueue(element);
                    continue;
                }

                problems.Add($"{type.Name}.{property.Name}: {element.Name}");
            }
        }

        return problems;
    }

    private static Type ElementOf(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsArray && underlying.GetElementType() != typeof(char))
        {
            return ElementOf(underlying.GetElementType()!);
        }

        if (underlying.IsGenericType
            && underlying.GetGenericTypeDefinition() is var definition
            && (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(List<>)))
        {
            return ElementOf(underlying.GetGenericArguments()[0]);
        }

        return underlying;
    }

    private readonly record struct ProbeTileV1(string Address);

    private sealed record ProbeV1(
        Guid Camera,
        string Name,
        object Loose,
        System.Text.Json.JsonElement Element,
        char[] Characters,
        ProbeTileV1 Tile,
        IReadOnlyList<int> Numbers);
}
