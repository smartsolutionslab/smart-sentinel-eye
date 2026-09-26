using System.Reflection;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

public class V1ResourceMapTests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        null,
        null);

    private readonly V1ResourceMap map = V1ResourceMap.Default;

    [Fact]
    public void Camera_V1_maps_to_camera_resource_kind()
    {
        Guid id = Guid.CreateVersion7();
        CameraRegisteredV1 evt = new(
            id, "north-gate", "rtsp://example/cam",
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        V1Mapping mapping = map.Lookup(typeof(CameraRegisteredV1), evt);

        mapping.Kind.HasValue.ShouldBeTrue();
        mapping.Kind.Value.ShouldBe(ResourceKind.Camera);
        mapping.ResourceIdentifier.HasValue.ShouldBeTrue();
        mapping.ResourceIdentifier.Value.Value.ShouldBe(id.ToString());
    }

    [Fact]
    public void Device_V1_picks_clientId_not_the_RegisteredClientIdentifier()
    {
        DeviceRegisteredV1 evt = new(
            Guid.CreateVersion7(), "plc-station-4", "plc", "station-4", "munich",
            DateTimeOffset.UtcNow, Metadata: TestMetadata);

        V1Mapping mapping = map.Lookup(typeof(DeviceRegisteredV1), evt);

        mapping.Kind.Value.ShouldBe(ResourceKind.Device);
        mapping.ResourceIdentifier.Value.Value.ShouldBe("plc-station-4");
    }

    [Fact]
    public void Kiosk_V1_maps_to_kiosk_via_a_hand_tweak()
    {
        KioskEnrolledV1 evt = new(
            Guid.CreateVersion7(), "kiosk-pilot", "munich", DateTimeOffset.UtcNow, Metadata: TestMetadata);

        V1Mapping mapping = map.Lookup(typeof(KioskEnrolledV1), evt);

        mapping.Kind.Value.ShouldBe(ResourceKind.Kiosk);
        mapping.ResourceIdentifier.Value.Value.ShouldBe("kiosk-pilot");
    }

    [Fact]
    public void AuditChunkArchivedV1_pivots_on_the_chunk_identifier()
    {
        Guid chunkId = Guid.CreateVersion7();
        AuditChunkArchivedV1 evt = new(
            chunkId, "munich", 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, "k", "m", Metadata: TestMetadata);

        V1Mapping mapping = map.Lookup(typeof(AuditChunkArchivedV1), evt);

        mapping.Kind.HasValue.ShouldBeTrue();
        mapping.ResourceIdentifier.Value.Value.ShouldBe(chunkId.ToString());
    }

    [Fact]
    public void Lookup_of_a_non_IIntegrationEvent_type_returns_unmapped()
    {
        V1Mapping mapping = map.Lookup(typeof(string), "anything");
        mapping.Kind.HasValue.ShouldBeFalse();
        mapping.ResourceIdentifier.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void MappedTypes_covers_a_meaningful_subset_of_Shared_Contracts_V1s()
    {
        // Sanity bound: there are several specs' worth of V1s on
        // develop; if the convention scanner ever silently broke
        // (e.g. namespace-tail dictionary lost an entry), this
        // floor catches it well before the strict architecture
        // test in spec 009 T070.
        map.MappedTypes.Count.ShouldBeGreaterThanOrEqualTo(10);
        map.MappedTypes.ShouldContain(typeof(CameraRegisteredV1));
        map.MappedTypes.ShouldContain(typeof(AuditChunkArchivedV1));
    }

    public sealed record MappingCase(
        Type ContractType,
        ResourceKind ExpectedKind,
        Func<object> Factory,
        string ExpectedIdentifier,
        string? MustNotBeIdentifier = null)
    {
        public override string ToString() => ContractType.Name;
    }

    /// <summary>
    /// Spec 206 US3 — one row per concrete <c>IIntegrationEvent</c> in
    /// <c>Shared.Contracts</c>, pinning both the expected
    /// <see cref="ResourceKind"/> and the exact property the identifier is
    /// picked from. Every field of every factory-built instance is stamped
    /// with a distinct sentinel so no assertion can pass by coincidence.
    ///
    /// <para>
    /// The expected values below are transcribed by hand from each
    /// contract's declared shape (spec.md §"The complete audit"), never
    /// read back from <see cref="V1ResourceMap"/> — an assertion that
    /// checks its own input cannot fail.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<MappingCase> AllCases =
    [
        AuditChunkArchivedCase(),
        CameraAddressChangedCase(),
        CameraRegisteredCase(),
        CameraRenamedCase(),
        CameraRetiredCase(),
        FabEventIngestedCase(),
        DeviceRegisteredCase(),
        KioskEnrolledCase(),
        WebhookIntegrationRotatedCase(),
        LayoutRevisionArchivedCase(),
        LayoutRevisionPublishedCase(),
        OverlayHighlightRequestedCase(),
        OverlayRevisionArchivedCase(),
        OverlayRevisionPublishedCase(),
        StreamHealthChangedCase(),
        ResolvedOverlayTextChangedCase(),
        SystemVariableArchivedCase(),
        SystemVariableDefinedCase(),
        SystemVariableValueChangedCase(),
        SystemVariableValueRequestedCase(),
        WallConfiguredCase(),
        WallSceneChangedCase(),
    ];

    // 1. AuditObservability.AuditChunkArchivedV1 -> event / ChunkIdentifier (hand-tweak; blessed)
    private static MappingCase AuditChunkArchivedCase()
    {
        Guid chunkIdentifier = Guid.CreateVersion7();
        return new MappingCase(
            typeof(AuditChunkArchivedV1),
            ResourceKind.Event,
            () => new AuditChunkArchivedV1(
                chunkIdentifier,
                "FabId-sentinel",
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                DateTimeOffset.UtcNow.AddSeconds(2),
                DateTimeOffset.UtcNow.AddSeconds(3),
                "ArchiveObjectKey-sentinel",
                "ContentMd5-sentinel",
                TestMetadata),
            chunkIdentifier.ToString());
    }

    // 2. CameraCatalog.CameraAddressChangedV1 -> camera / Camera
    private static MappingCase CameraAddressChangedCase()
    {
        Guid camera = Guid.CreateVersion7();
        return new MappingCase(
            typeof(CameraAddressChangedV1),
            ResourceKind.Camera,
            () => new CameraAddressChangedV1(
                camera,
                "Fab-sentinel",
                "PreviousUrl-sentinel",
                "Url-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            camera.ToString());
    }

    // 3. CameraCatalog.CameraRegisteredV1 -> camera / Camera
    private static MappingCase CameraRegisteredCase()
    {
        Guid camera = Guid.CreateVersion7();
        return new MappingCase(
            typeof(CameraRegisteredV1),
            ResourceKind.Camera,
            () => new CameraRegisteredV1(
                camera,
                "Name-sentinel",
                "Url-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            camera.ToString());
    }

    // 4. CameraCatalog.CameraRenamedV1 -> camera / Camera
    private static MappingCase CameraRenamedCase()
    {
        Guid camera = Guid.CreateVersion7();
        return new MappingCase(
            typeof(CameraRenamedV1),
            ResourceKind.Camera,
            () => new CameraRenamedV1(
                camera,
                "Fab-sentinel",
                "PreviousName-sentinel",
                "Name-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            camera.ToString());
    }

    // 5. CameraCatalog.CameraRetiredV1 -> camera / Camera
    private static MappingCase CameraRetiredCase()
    {
        Guid camera = Guid.CreateVersion7();
        return new MappingCase(
            typeof(CameraRetiredV1),
            ResourceKind.Camera,
            () => new CameraRetiredV1(
                camera,
                "Fab-sentinel",
                "Name-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            camera.ToString());
    }

    // 6. EventIngestion.FabEventIngestedV1 -> event / EventIdentifier
    private static MappingCase FabEventIngestedCase()
    {
        Guid eventIdentifier = Guid.CreateVersion7();
        return new MappingCase(
            typeof(FabEventIngestedV1),
            ResourceKind.Event,
            () => new FabEventIngestedV1(
                eventIdentifier,
                "Fab-sentinel",
                "Source-sentinel",
                "Device-sentinel",
                "Kind-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                DateTimeOffset.UtcNow.AddSeconds(2),
                "Payload-sentinel",
                TestMetadata),
            eventIdentifier.ToString());
    }

    // 7. Identity.DeviceRegisteredV1 -> device / ClientId (hand-tweak)
    private static MappingCase DeviceRegisteredCase()
    {
        string clientId = "ClientId-sentinel";
        return new MappingCase(
            typeof(DeviceRegisteredV1),
            ResourceKind.Device,
            () => new DeviceRegisteredV1(
                Guid.CreateVersion7(),
                clientId,
                "DeviceType-sentinel",
                "DeviceIdentifier-sentinel",
                "Fab-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                TestMetadata),
            clientId);
    }

    // 8. Identity.KioskEnrolledV1 -> kiosk / ClientId (hand-tweak)
    private static MappingCase KioskEnrolledCase()
    {
        string clientId = "ClientId-sentinel";
        return new MappingCase(
            typeof(KioskEnrolledV1),
            ResourceKind.Kiosk,
            () => new KioskEnrolledV1(
                Guid.CreateVersion7(),
                clientId,
                "Fab-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                TestMetadata),
            clientId);
    }

    // 9. Identity.WebhookIntegrationRotatedV1 -> webhook-integration / IntegrationName (hand-tweak)
    private static MappingCase WebhookIntegrationRotatedCase()
    {
        string integrationName = "IntegrationName-sentinel";
        return new MappingCase(
            typeof(WebhookIntegrationRotatedV1),
            ResourceKind.WebhookIntegration,
            () => new WebhookIntegrationRotatedV1(
                integrationName,
                "ClientId-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                TestMetadata),
            integrationName);
    }

    // 10. LayoutComposition.LayoutRevisionArchivedV1 -> layout / Layout
    private static MappingCase LayoutRevisionArchivedCase()
    {
        Guid layout = Guid.CreateVersion7();
        return new MappingCase(
            typeof(LayoutRevisionArchivedV1),
            ResourceKind.Layout,
            () => new LayoutRevisionArchivedV1(
                layout,
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            layout.ToString());
    }

    // 11. LayoutComposition.LayoutRevisionPublishedV2 -> layout / Layout
    private static MappingCase LayoutRevisionPublishedCase()
    {
        Guid layout = Guid.CreateVersion7();
        return new MappingCase(
            typeof(LayoutRevisionPublishedV2),
            ResourceKind.Layout,
            () => new LayoutRevisionPublishedV2(
                layout,
                1,
                "Name-sentinel",
                [new LayoutTileV2(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, 0)],
                2,
                3,
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            layout.ToString());
    }

    // 12. LayoutComposition.OverlayHighlightRequestedV1 -> DEFECT A: expected overlay / OverlayIdentifier.
    //     Today's map wrongly pivots this on kind `layout` (the publishing namespace), not `overlay`
    //     (the event's subject). The picked identifier (OverlayIdentifier) is already correct.
    private static MappingCase OverlayHighlightRequestedCase()
    {
        Guid overlayIdentifier = Guid.CreateVersion7();
        Guid causingEventIdentifier = Guid.CreateVersion7();
        return new MappingCase(
            typeof(OverlayHighlightRequestedV1),
            ResourceKind.Overlay,
            () => new OverlayHighlightRequestedV1(
                overlayIdentifier,
                1500,
                DateTimeOffset.UtcNow.AddSeconds(1),
                causingEventIdentifier,
                TestMetadata),
            overlayIdentifier.ToString(),
            causingEventIdentifier.ToString());
    }

    // 13. OverlayDesigner.OverlayRevisionArchivedV1 -> overlay / Overlay
    private static MappingCase OverlayRevisionArchivedCase()
    {
        Guid overlay = Guid.CreateVersion7();
        return new MappingCase(
            typeof(OverlayRevisionArchivedV1),
            ResourceKind.Overlay,
            () => new OverlayRevisionArchivedV1(
                overlay,
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            overlay.ToString());
    }

    // 14. OverlayDesigner.OverlayRevisionPublishedV1 -> overlay / Overlay
    private static MappingCase OverlayRevisionPublishedCase()
    {
        Guid overlay = Guid.CreateVersion7();
        return new MappingCase(
            typeof(OverlayRevisionPublishedV1),
            ResourceKind.Overlay,
            () => new OverlayRevisionPublishedV1(
                overlay,
                1,
                "Name-sentinel",
                "Text-sentinel",
                1.1m,
                2.2m,
                3.3m,
                4.4m,
                16,
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            overlay.ToString());
    }

    // 15. StreamDistribution.StreamHealthChangedV1 -> stream / Camera (blessed: the stream's public
    //     handle is the camera id, GET /streams/{cameraIdentifier})
    private static MappingCase StreamHealthChangedCase()
    {
        Guid camera = Guid.CreateVersion7();
        return new MappingCase(
            typeof(StreamHealthChangedV1),
            ResourceKind.Stream,
            () => new StreamHealthChangedV1(
                camera,
                "FromState-sentinel",
                "ToState-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                "Error-sentinel",
                TestMetadata),
            camera.ToString());
    }

    // 16. SystemVariables.ResolvedOverlayTextChangedV1 -> overlay / Overlay (hand-tweak)
    private static MappingCase ResolvedOverlayTextChangedCase()
    {
        Guid overlay = Guid.CreateVersion7();
        return new MappingCase(
            typeof(ResolvedOverlayTextChangedV1),
            ResourceKind.Overlay,
            () => new ResolvedOverlayTextChangedV1(
                overlay,
                "ResolvedText-sentinel",
                7L,
                TestMetadata),
            overlay.ToString());
    }

    // 17. SystemVariables.SystemVariableArchivedV1 -> variable / Variable
    private static MappingCase SystemVariableArchivedCase()
    {
        Guid variable = Guid.CreateVersion7();
        return new MappingCase(
            typeof(SystemVariableArchivedV1),
            ResourceKind.Variable,
            () => new SystemVariableArchivedV1(
                variable,
                "Name-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            variable.ToString());
    }

    // 18. SystemVariables.SystemVariableDefinedV1 -> variable / Variable
    private static MappingCase SystemVariableDefinedCase()
    {
        Guid variable = Guid.CreateVersion7();
        return new MappingCase(
            typeof(SystemVariableDefinedV1),
            ResourceKind.Variable,
            () => new SystemVariableDefinedV1(
                variable,
                "Name-sentinel",
                "Type-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            variable.ToString());
    }

    // 19. SystemVariables.SystemVariableValueChangedV1 -> variable / Variable
    private static MappingCase SystemVariableValueChangedCase()
    {
        Guid variable = Guid.CreateVersion7();
        return new MappingCase(
            typeof(SystemVariableValueChangedV1),
            ResourceKind.Variable,
            () => new SystemVariableValueChangedV1(
                variable,
                "Name-sentinel",
                "Type-sentinel",
                "Value-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                Guid.CreateVersion7(),
                TestMetadata),
            variable.ToString());
    }

    // 20. SystemVariables.SystemVariableValueRequestedV1 -> DEFECT B: expected variable / Name.
    //     The contract carries no variable guid; SystemVariables addresses every route by
    //     {name}. Today's only Guid on the contract is CausingEventIdentifier, so the
    //     reflection picker wrongly takes that instead of falling back to the Name allow-list
    //     entry (the fallback never runs once a Guid property is found).
    private static MappingCase SystemVariableValueRequestedCase()
    {
        string name = "Name-sentinel";
        Guid causingEventIdentifier = Guid.CreateVersion7();
        return new MappingCase(
            typeof(SystemVariableValueRequestedV1),
            ResourceKind.Variable,
            () => new SystemVariableValueRequestedV1(
                name,
                "Value-sentinel",
                DateTimeOffset.UtcNow.AddSeconds(1),
                causingEventIdentifier,
                TestMetadata),
            name,
            causingEventIdentifier.ToString());
    }

    // 21. LayoutComposition.WallConfiguredV1 -> wall / Wall (spec 258 US1, T012)
    private static MappingCase WallConfiguredCase()
    {
        Guid wall = Guid.CreateVersion7();
        return new MappingCase(
            typeof(WallConfiguredV1),
            ResourceKind.Wall,
            () => new WallConfiguredV1(
                wall,
                "Name-sentinel",
                [Guid.CreateVersion7(), Guid.CreateVersion7()],
                Guid.CreateVersion7(),
                DateTimeOffset.UtcNow.AddSeconds(1),
                TestMetadata),
            wall.ToString());
    }

    // 22. LayoutComposition.WallSceneChangedV1 -> wall / Wall (spec 258 US1, T012)
    private static MappingCase WallSceneChangedCase()
    {
        Guid wall = Guid.CreateVersion7();
        return new MappingCase(
            typeof(WallSceneChangedV1),
            ResourceKind.Wall,
            () => new WallSceneChangedV1(
                wall,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                1L,
                "Operator",
                null,
                null,
                DateTimeOffset.UtcNow.AddSeconds(1),
                TestMetadata),
            wall.ToString());
    }

    public static TheoryData<MappingCase> Cases()
    {
        TheoryData<MappingCase> data = [];
        foreach (MappingCase mappingCase in AllCases)
        {
            data.Add(mappingCase);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_integration_event_pivots_on_the_resource_it_is_about(MappingCase mappingCase)
    {
        object instance = mappingCase.Factory();
        mappingCase.ContractType.IsInstanceOfType(instance).ShouldBeTrue();

        V1Mapping mapping = map.Lookup(mappingCase.ContractType, instance);

        mapping.Kind.HasValue.ShouldBeTrue();
        mapping.Kind.Value.ShouldBe(mappingCase.ExpectedKind);
        mapping.ResourceIdentifier.HasValue.ShouldBeTrue();
        mapping.ResourceIdentifier.Value.Value.ShouldBe(mappingCase.ExpectedIdentifier);

        if (mappingCase.MustNotBeIdentifier is not null)
        {
            mapping.ResourceIdentifier.Value.Value.ShouldNotBe(mappingCase.MustNotBeIdentifier);
        }
    }

    /// <summary>
    /// Spec 206 US3 — completeness in the forward direction. A 21st
    /// concrete <c>IIntegrationEvent</c> with no table row fails here,
    /// naming itself.
    /// </summary>
    [Fact]
    public void Every_integration_event_has_a_row_in_the_mapping_table()
    {
        Assembly contracts = typeof(IIntegrationEvent).Assembly;
        HashSet<Type> allIntegrationEvents = [.. contracts.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type))];

        HashSet<Type> tableTypes = [.. AllCases.Select(mappingCase => mappingCase.ContractType)];

        IReadOnlyList<Type> missing = [.. allIntegrationEvents
            .Except(tableTypes)
            .Where(type => !map.ExplicitlyOptedOut.Contains(type.Name))];

        missing.ShouldBeEmpty(
            $"No mapping-table row for: {string.Join(", ", missing.Select(type => type.FullName))}. Add a MappingCase in V1ResourceMapTests.AllCases.");
    }

    /// <summary>
    /// Spec 206 US3 — completeness in the reverse direction. A table row
    /// naming a type <see cref="V1ResourceMap"/> does not map fails here.
    /// </summary>
    [Fact]
    public void Every_mapping_table_row_names_a_mapped_type()
    {
        HashSet<Type> mappedTypes = [.. map.MappedTypes];

        IReadOnlyList<Type> unmapped = [.. AllCases
            .Select(mappingCase => mappingCase.ContractType)
            .Where(type => !mappedTypes.Contains(type))];

        unmapped.ShouldBeEmpty(
            $"Mapping-table row names a type V1ResourceMap.Default does not map: {string.Join(", ", unmapped.Select(type => type.FullName))}.");
    }
}
