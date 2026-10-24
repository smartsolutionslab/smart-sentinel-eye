using System.Reflection;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 377 (#2977, FR-007): the audit payload redaction is an explicit register
/// (<c>AuditPayloadRedaction.UrlFields</c>), so the way a new contract field slips past it is by being
/// named something a name match would miss. This guard does not look for URLs. It demands a decision for
/// every <c>string</c> or <c>Uri</c> property or public field of every concrete <c>IIntegrationEvent</c> in
/// <c>Shared.Contracts</c>, nested records and collection elements included: each is either a redacted URL
/// field (the register, top level only) or listed in <see cref="VerbatimInventory"/> below, where a
/// reviewer sees it in the diff.
///
/// <para>
/// The register is read by reflection, by name, so this class compiles before the type exists; until then
/// the classification fact fails on an assertion that says so.
/// </para>
///
/// <para>
/// Counterfactuals (prove-a-guard-by-counterfactual): <see cref="ProbeV1"/> hides a stream address behind
/// the name <c>StreamEndpoint</c> and another behind a nested record; the classification function must
/// report both as unclassified, and must report a field that is both redacted and verbatim, a register
/// entry that names no property, a nested field classified as a URL, and an inventory entry that no longer
/// exists.
/// </para>
/// </summary>
public class AuditPayloadRedactionCoverageTests
{
    private const string RedactionTypeName =
        "SmartSentinelEye.AuditObservability.Application.EventHandlers.AuditPayloadRedaction";

    /// <summary>
    /// Every string or Uri property of an audited contract that is deliberately stored as the wire carries
    /// it. <c>Type.Property</c>. A camera address never belongs here: it goes in the register.
    /// </summary>
    private static readonly IReadOnlySet<string> VerbatimInventory = new HashSet<string>(StringComparer.Ordinal)
    {
        "AuditChunkArchivedV1.ArchiveObjectKey",
        "AuditChunkArchivedV1.ContentMd5",
        "AuditChunkArchivedV1.FabId",
        "CameraAddressChangedV1.Fab",
        "CameraAddressReadV1.Fab",
        "CameraRegisteredV1.Name",
        "CameraRenamedV1.Fab",
        "CameraRenamedV1.Name",
        "CameraRenamedV1.PreviousName",
        "CameraRetiredV1.Fab",
        "CameraRetiredV1.Name",
        "DeviceRegisteredV1.ClientId",
        "DeviceRegisteredV1.DeviceIdentifier",
        "DeviceRegisteredV1.DeviceType",
        "DeviceRegisteredV1.Fab",
        "EventMetadata.Fab",
        "FabEventIngestedV1.Device",
        "FabEventIngestedV1.Fab",
        "FabEventIngestedV1.Kind",
        "FabEventIngestedV1.Payload",
        "FabEventIngestedV1.Source",
        "KioskEnrolledV1.ClientId",
        "KioskEnrolledV1.Fab",
        "LayoutRevisionPublishedV2.Name",
        "OverlayElementV3.Color",
        "OverlayElementV3.Kind",
        "OverlayElementV3.Text",
        "OverlayRevisionPublishedV3.Name",
        "ResolvedOverlayTextV3.Resolved",
        "ResolvedOverlayTextV3.Template",
        "StreamHealthChangedV1.Error",
        "StreamHealthChangedV1.FromState",
        "StreamHealthChangedV1.ToState",
        "SystemVariableArchivedV1.Name",
        "SystemVariableDefinedV1.Name",
        "SystemVariableDefinedV1.Type",
        "SystemVariableValueChangedV1.Name",
        "SystemVariableValueChangedV1.Type",
        "SystemVariableValueChangedV1.Value",
        "SystemVariableValueRequestedV1.Name",
        "SystemVariableValueRequestedV1.Value",
        "WallConfiguredV1.Name",
        "WallSceneChangedV1.Cause",
        "WallSceneSwitchRequestedV1.Target",
        "WebhookIntegrationRegisteredV1.DefaultKind",
        "WebhookIntegrationRegisteredV1.IntegrationName",
        "WebhookIntegrationRevokedV1.IntegrationName",
        "WebhookIntegrationRotatedV1.ClientId",
        "WebhookIntegrationRotatedV1.IntegrationName",
    };

    [Fact]
    public void Every_string_field_of_every_integration_event_is_classified_for_audit()
    {
        var register = ReadRegister();

        var problems = Classify(
            IntegrationEventTypes(),
            register,
            VerbatimInventory);

        problems.ShouldBeEmpty(
            "Every string or Uri property of an audited integration event must be either registered in "
            + "AuditPayloadRedaction.UrlFields (a camera address, redacted to its origin) or listed in "
            + "AuditPayloadRedactionCoverageTests.VerbatimInventory (stored as sent):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_redacted_url_fields_are_exactly_the_camera_addresses()
    {
        var register = ReadRegister();

        IReadOnlySet<string> redacted = new HashSet<string>(
            register.SelectMany(entry => entry.Value.Select(property => $"{entry.Key.Name}.{property}")),
            StringComparer.Ordinal);

        redacted.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(
        [
            "CameraAddressChangedV1.PreviousUrl",
            "CameraAddressChangedV1.Url",
            "CameraRegisteredV1.Url",
        ]);

        // A registered Uri or collection field is not what the redaction rewrites, and would throw on every delivery.
        foreach (var (type, names) in register)
        {
            foreach (var name in names)
            {
                type.GetProperty(name)?.PropertyType.ShouldBe(typeof(string), $"{type.Name}.{name}");
            }
        }
    }

    [Fact]
    public void The_classification_reports_a_stream_address_that_is_named_innocently_and_one_nested_in_a_record()
    {
        var problems = Classify([typeof(ProbeV1)], new Dictionary<Type, IReadOnlyList<string>>(), new HashSet<string>());

        problems.ShouldContain(problem => problem.Contains("ProbeV1.StreamEndpoint", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("ProbeTileV1.Address", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("ProbeTileV1.Labels", StringComparison.Ordinal));
        problems.Count.ShouldBe(3);
    }

    [Fact]
    public void The_classification_is_satisfied_when_every_probe_field_is_decided()
    {
        Dictionary<Type, IReadOnlyList<string>> register = new() { [typeof(ProbeV1)] = [nameof(ProbeV1.StreamEndpoint)] };
        HashSet<string> verbatim = ["ProbeTileV1.Address", "ProbeTileV1.Labels"];

        Classify([typeof(ProbeV1)], register, verbatim).ShouldBeEmpty();
    }

    [Fact]
    public void The_classification_reports_a_field_that_is_both_redacted_and_verbatim()
    {
        Dictionary<Type, IReadOnlyList<string>> register = new() { [typeof(ProbeV1)] = [nameof(ProbeV1.StreamEndpoint)] };
        HashSet<string> verbatim = ["ProbeV1.StreamEndpoint", "ProbeTileV1.Address", "ProbeTileV1.Labels"];

        var problems = Classify([typeof(ProbeV1)], register, verbatim);

        problems.Count.ShouldBe(1);
        problems[0].ShouldContain("ProbeV1.StreamEndpoint");
    }

    [Fact]
    public void The_classification_reports_a_register_entry_that_names_no_string_property()
    {
        Dictionary<Type, IReadOnlyList<string>> register = new()
        {
            [typeof(ProbeV1)] = [nameof(ProbeV1.StreamEndpoint), "Gone", nameof(ProbeV1.Port)],
        };
        HashSet<string> verbatim = ["ProbeTileV1.Address", "ProbeTileV1.Labels"];

        var problems = Classify([typeof(ProbeV1)], register, verbatim);

        problems.Count.ShouldBe(2);
        problems.ShouldContain(problem => problem.Contains("ProbeV1.Gone", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("ProbeV1.Port", StringComparison.Ordinal));
    }

    [Fact]
    public void The_classification_reports_a_nested_field_classified_as_a_url()
    {
        Dictionary<Type, IReadOnlyList<string>> register = new()
        {
            [typeof(ProbeV1)] = [nameof(ProbeV1.StreamEndpoint)],
            [typeof(ProbeTileV1)] = [nameof(ProbeTileV1.Address)],
        };
        HashSet<string> verbatim = ["ProbeTileV1.Labels"];

        var problems = Classify([typeof(ProbeV1)], register, verbatim);

        problems.Count.ShouldBe(1);
        problems[0].ShouldContain("nested URL fields are not supported by AuditPayloadRedaction");
        problems[0].ShouldContain("ProbeTileV1.Address");
    }

    [Fact]
    public void The_classification_reports_an_inventory_entry_that_no_longer_exists()
    {
        Dictionary<Type, IReadOnlyList<string>> register = new() { [typeof(ProbeV1)] = [nameof(ProbeV1.StreamEndpoint)] };
        HashSet<string> verbatim = ["ProbeTileV1.Address", "ProbeTileV1.Labels", "ProbeV1.Removed"];

        var problems = Classify([typeof(ProbeV1)], register, verbatim);

        problems.Count.ShouldBe(1);
        problems[0].ShouldContain("ProbeV1.Removed");
    }

    public static TheoryData<Type, string> HiddenShapes() =>
        new()
        {
            { typeof(ProbeListOfListsV1), "ProbeListOfListsV1.Values" },
            { typeof(ProbeJaggedV1), "ProbeJaggedV1.Values" },
            { typeof(ProbeArrayOfListsV1), "ProbeArrayOfListsV1.Values" },
            { typeof(ProbeListOfLeafArraysV1), "ProbeLeafV1.Address" },
            { typeof(ProbeJaggedLeavesV1), "ProbeLeafV1.Address" },
            { typeof(ProbeFieldV1), "ProbeFieldV1.Endpoint" },
            { typeof(ProbeFieldHolderV1), "ProbeLeafV1.Address" },
        };

    [Theory]
    [MemberData(nameof(HiddenShapes))]
    public void The_classification_reports_a_string_hidden_in_a_nested_collection_or_a_public_field(Type probe, string expected)
    {
        var problems = Classify([probe], new Dictionary<Type, IReadOnlyList<string>>(), new HashSet<string>());

        problems.ShouldContain(problem => problem.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_classification_reports_two_audited_types_that_share_a_short_name()
    {
        HashSet<string> verbatim = ["DuplicateV1.Name"];

        var problems = Classify([typeof(ProbeDuplicatesV1)], new Dictionary<Type, IReadOnlyList<string>>(), verbatim);

        problems.Count.ShouldBe(1);
        problems[0].ShouldContain("DuplicateV1");
        problems[0].ShouldContain("short name");
    }

    private static IReadOnlyDictionary<Type, IReadOnlyList<string>> ReadRegister()
    {
        var application = Assembly.Load("SmartSentinelEye.AuditObservability.Application");
        var redaction = application.GetType(RedactionTypeName);
        redaction.ShouldNotBeNull($"{RedactionTypeName} is not defined; the audit payload redaction register does not exist.");

        var property = redaction.GetProperty("UrlFields", BindingFlags.Public | BindingFlags.Static);
        property.ShouldNotBeNull($"{RedactionTypeName}.UrlFields (public static) is not defined.");

        return property.GetValue(null)!
            .ShouldBeAssignableTo<IReadOnlyDictionary<Type, IReadOnlyList<string>>>();
    }

    private static IReadOnlyList<Type> IntegrationEventTypes()
    {
        var contracts = Assembly.Load("SmartSentinelEye.Shared.Contracts");
        var integrationEvent = contracts.GetType("SmartSentinelEye.Shared.Contracts.IIntegrationEvent")!;

        return
        [
            .. contracts.GetTypes()
                .Where(type => !type.IsAbstract && !type.IsInterface)
                .Where(type => integrationEvent.IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A decision for every string or Uri property reachable from <paramref name="events"/>, through
    /// nested records and collection elements. Returns one message per problem; empty means classified.
    /// </summary>
    private static List<string> Classify(
        IEnumerable<Type> events,
        IReadOnlyDictionary<Type, IReadOnlyList<string>> register,
        IReadOnlySet<string> verbatim)
    {
        var topLevel = events.ToHashSet();
        Dictionary<Type, bool> reachable = [];
        foreach (var root in topLevel)
        {
            Walk(root, isTopLevel: true, reachable);
        }

        List<string> problems = [];
        foreach (var group in reachable.Keys.GroupBy(type => type.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"{group.Key} is the short name of {group.Count()} audited types "
                + $"({string.Join(", ", group.Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal))}); "
                + "the verbatim inventory is keyed by short name, so rename one.");
        }

        HashSet<string> existing = [];
        foreach (var (type, isTopLevel) in reachable.OrderBy(entry => entry.Key.FullName, StringComparer.Ordinal))
        {
            foreach (var name in StringProperties(type))
            {
                var key = $"{type.Name}.{name}";
                existing.Add(key);
                var redacted = register.TryGetValue(type, out var names) && names.Contains(name);
                var listed = verbatim.Contains(key);

                if (redacted && listed)
                {
                    problems.Add($"{key} is both in the redaction register and in the verbatim inventory.");
                }
                else if (!redacted && !listed)
                {
                    problems.Add($"{key} is a string field nobody classified.");
                }
                else if (redacted && !isTopLevel)
                {
                    problems.Add($"{key}: nested URL fields are not supported by AuditPayloadRedaction; extend it.");
                }
            }
        }

        foreach (var (type, names) in register)
        {
            foreach (var name in names)
            {
                var key = $"{type.Name}.{name}";
                if (!existing.Contains(key))
                {
                    problems.Add($"{key} is in the redaction register but is not a string field of an audited contract.");
                }
            }
        }

        problems.AddRange(verbatim
            .Where(key => !existing.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => $"{key} is in the verbatim inventory but no such string field exists any more."));

        return problems;
    }

    private static void Walk(Type type, bool isTopLevel, Dictionary<Type, bool> reachable)
    {
        if (reachable.TryGetValue(type, out var seenTopLevel))
        {
            reachable[type] = seenTopLevel || isTopLevel;
            return;
        }

        reachable[type] = isTopLevel;
        foreach (var (_, memberType) in Members(type))
        {
            var element = ElementTypeOf(memberType);
            if (element.Assembly == type.Assembly && element.IsClass && !element.IsAbstract)
            {
                Walk(element, isTopLevel: false, reachable);
            }
        }
    }

    private static IEnumerable<string> StringProperties(Type type) =>
        Members(type)
            .Where(member => ElementTypeOf(member.Type) is var element
                && (element == typeof(string) || element == typeof(Uri)))
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static IEnumerable<(string Name, Type Type)> Members(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, Type: property.PropertyType))
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (field.Name, Type: field.FieldType)));

    private static Type ElementTypeOf(Type type)
    {
        if (type.IsArray)
        {
            return ElementTypeOf(type.GetElementType()!);
        }

        if (type != typeof(string) && type.IsGenericType)
        {
            return ElementTypeOf(type.GetGenericArguments()[^1]);
        }

        return type;
    }

    private sealed record ProbeTileV1(string Address, IReadOnlyList<string> Labels);

    private sealed record ProbeV1(string StreamEndpoint, int Port, ProbeTileV1 Tile, IReadOnlyList<ProbeTileV1> Tiles);

    private sealed record ProbeListOfListsV1(IReadOnlyList<IReadOnlyList<string>> Values);

    private sealed record ProbeJaggedV1(string[][] Values);

    private sealed record ProbeArrayOfListsV1(IReadOnlyList<string>[] Values);

    private sealed record ProbeListOfLeafArraysV1(IReadOnlyList<ProbeLeafV1[]> Leaves);

    private sealed record ProbeJaggedLeavesV1(ProbeLeafV1[][] Leaves);

    private static class ProbeFirst
    {
        public sealed record DuplicateV1(string Name);
    }

    private static class ProbeSecond
    {
        public sealed record DuplicateV1(string Name);
    }

    private sealed record ProbeDuplicatesV1(ProbeFirst.DuplicateV1 First, ProbeSecond.DuplicateV1 Second);
}
