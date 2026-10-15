using System.Reflection;
using System.Text.Json.Serialization;

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

    private static readonly Type[] OnTypes = [typeof(JsonPolymorphicAttribute), typeof(JsonDerivedTypeAttribute)];

    private static readonly Type[] OnMembers = [typeof(JsonIncludeAttribute), typeof(JsonExtensionDataAttribute)];

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

    [Fact]
    public void The_shape_check_reports_a_public_field_the_classification_cannot_see()
    {
        Unrecognised([typeof(ProbeFieldsV1)], typeof(ProbeFieldsV1).Assembly)
            .ShouldBe(["ProbeFieldsV1.Loose: Object"]);
    }

    [Fact]
    public void No_audited_contract_overrides_how_it_is_serialised()
    {
        var contracts = Assembly.Load("SmartSentinelEye.Shared.Contracts");
        var integrationEvent = contracts.GetType("SmartSentinelEye.Shared.Contracts.IIntegrationEvent")!;

        var problems = SerialisationOverrides(
            contracts.GetTypes().Where(type => !type.IsAbstract && !type.IsInterface && integrationEvent.IsAssignableFrom(type)),
            contracts);

        problems.ShouldBeEmpty(
            "An audited contract changes what System.Text.Json writes in a way the redaction and its guards "
            + "do not model (a hidden member, an extension bag or a polymorphic payload):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_override_check_reports_each_serialisation_attribute_the_redaction_does_not_model()
    {
        var assembly = typeof(ProbeV1).Assembly;

        SerialisationOverrides([typeof(ProbeIncludedV1)], assembly).ShouldBe(["ProbeIncludedV1.Hidden: JsonIncludeAttribute"]);
        SerialisationOverrides([typeof(ProbeExtensionV1)], assembly).ShouldBe(["ProbeExtensionV1.Rest: JsonExtensionDataAttribute"]);
        SerialisationOverrides([typeof(ProbePolymorphicV1)], assembly)
            .ShouldBe(["ProbePolymorphicV1: JsonPolymorphicAttribute", "ProbePolymorphicV1: JsonDerivedTypeAttribute"], ignoreOrder: true);
    }

    private static List<string> Unrecognised(IEnumerable<Type> roots, Assembly contracts)
    {
        List<string> problems = [];

        foreach (var type in Reachable(roots, contracts))
        {
            foreach (var (name, memberType) in Members(type))
            {
                var element = ElementOf(memberType);
                if (Scalars.Contains(element) || IsContractClass(element, contracts))
                {
                    continue;
                }

                problems.Add($"{type.Name}.{name}: {element.Name}");
            }
        }

        return problems;
    }

    private static List<string> SerialisationOverrides(IEnumerable<Type> roots, Assembly contracts)
    {
        List<string> problems = [];

        foreach (var type in Reachable(roots, contracts))
        {
            problems.AddRange(OnTypes
                .Where(attribute => type.IsDefined(attribute, inherit: true))
                .Select(attribute => $"{type.Name}: {attribute.Name}"));

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                problems.AddRange(OnMembers
                    .Where(attribute => member.IsDefined(attribute, inherit: true))
                    .Select(attribute => $"{type.Name}.{member.Name}: {attribute.Name}"));
            }
        }

        return problems;
    }

    private static HashSet<Type> Reachable(IEnumerable<Type> roots, Assembly contracts)
    {
        HashSet<Type> seen = [];
        Queue<Type> pending = new(roots);

        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var (_, memberType) in Members(type))
            {
                var element = ElementOf(memberType);
                if (IsContractClass(element, contracts))
                {
                    pending.Enqueue(element);
                }
            }
        }

        return seen;
    }

    private static bool IsContractClass(Type type, Assembly contracts) =>
        type.Assembly == contracts && type.IsClass && !type.IsAbstract;

    private static IEnumerable<(string Name, Type Type)> Members(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, Type: property.PropertyType))
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (field.Name, Type: field.FieldType)));

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

    private sealed record ProbeIncludedV1
    {
        [JsonInclude]
        internal string Hidden { get; init; } = string.Empty;
    }

    private sealed record ProbeExtensionV1
    {
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement> Rest { get; init; } = [];
    }

    [JsonPolymorphic]
    [JsonDerivedType(typeof(ProbeDerivedV1))]
    private record ProbePolymorphicV1(string Name);

    private sealed record ProbeDerivedV1(string Name) : ProbePolymorphicV1(Name);
}
