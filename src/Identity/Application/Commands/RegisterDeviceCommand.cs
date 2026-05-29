using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

/// <summary>
/// Registers a new non-human device (spec 008 US4). The
/// <see cref="DeviceType"/> is the spec 006 source wire string
/// (<c>"plc"</c> | <c>"inference"</c>); the resulting Keycloak
/// client gets <c>clientId = &lt;deviceType&gt;-&lt;deviceIdentifier&gt;</c>
/// so the MQTT ACL (FR-008) can verify topic-to-client binding.
/// </summary>
public sealed record RegisterDeviceCommand(
    string DeviceType,
    string DeviceIdentifier,
    FabIdentifier Fab,
    OperatorIdentifier RegisteredBy)
    : ICommand<Result<DeviceCredentialsDto, RegisterDeviceError>>;
