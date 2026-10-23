using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Architecture.Tests.Messaging;

/// <summary>
/// ADR-0185 decisions 2, 3 and 5. Each context declares the
/// integration events whose RabbitMQ send is durable, and this reads the answer off the module's
/// own registration, so a type dropped from a list, or added to the wrong one, fails here.
///
/// <para>
/// <b>Colours (ADR-0144).</b> The durable theory is <b>red</b> until phase 4b wires each module's
/// list. The non-durable theory is a <b>characterisation</b>, green before and after: every hot-path,
/// mixed-caller or Q3 type stays Inline in its own module (decision 5, which is what makes the
/// §IV latency claim N/A). It is proven able to fail by counterfactual 4 (add
/// <c>FabEventIngestedV1</c> to EventIngestion's list).
/// </para>
/// </summary>
public sealed class DurableOutboxListTests
{
    public static TheoryData<string, Type> DurableTypes() => new()
    {
        { "identity", typeof(WebhookIntegrationRotatedV1) },
        { "identity", typeof(DeviceRegisteredV1) },
        { "identity", typeof(KioskEnrolledV1) },
        { "identity", typeof(WebhookClientRaceDisableFailed) },
        { "camera-catalog", typeof(CameraRegisteredV1) },
        { "camera-catalog", typeof(CameraRenamedV1) },
        { "camera-catalog", typeof(CameraAddressChangedV1) },
        { "camera-catalog", typeof(CameraRetiredV1) },
        { "camera-catalog", typeof(CameraAddressReadV1) },
        { "layout-composition", typeof(LayoutRevisionPublishedV2) },
        { "layout-composition", typeof(LayoutRevisionArchivedV1) },
        { "layout-composition", typeof(WallConfiguredV1) },
        { "system-variables", typeof(SystemVariableDefinedV1) },
        { "system-variables", typeof(SystemVariableArchivedV1) },
        { "overlay-designer", typeof(OverlayRevisionPublishedV3) },
        { "overlay-designer", typeof(OverlayRevisionArchivedV1) },
        { "event-ingestion", typeof(WebhookIntegrationRevokedV1) },
    };

    public static TheoryData<string, Type> InlineTypes() => new()
    {
        { "event-ingestion", typeof(FabEventIngestedV1) },
        { "automation", typeof(SystemVariableValueRequestedV1) },
        { "automation", typeof(OverlayHighlightRequestedV1) },
        { "automation", typeof(WallSceneSwitchRequestedV1) },
        { "system-variables", typeof(SystemVariableValueChangedV1) },
        { "system-variables", typeof(ResolvedOverlayTextChangedV3) },
        { "layout-composition", typeof(WallSceneChangedV1) },
        { "stream-distribution", typeof(StreamHealthChangedV1) },
        { "audit-observability", typeof(AuditChunkArchivedV1) },
    };

    [Theory]
    [MemberData(nameof(DurableTypes))]
    public async Task A_declared_type_is_sent_durably_by_its_publishing_context(string contextName, Type messageType)
    {
        await using var composition = await DurableOutboxComposition.StartAsync(contextName);

        composition.IsDurable(messageType).ShouldBeTrue(
            $"{messageType.Name} is published by {contextName} for audit-before-disclosure or an admin change (ADR-0185 decision 3)");
    }

    [Theory]
    [MemberData(nameof(InlineTypes))]
    public async Task Every_hot_path_mixed_caller_or_Q3_type_stays_non_durable_in_its_own_module(string contextName, Type messageType)
    {
        await using var composition = await DurableOutboxComposition.StartAsync(contextName);

        composition.IsDurable(messageType).ShouldBeFalse(
            $"{messageType.Name} is on the hot path, mixed-caller or Q3 and belongs to #3103 (ADR-0185 decision 5)");
    }
}
