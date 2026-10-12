using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

/// <summary>
/// Enrolls a new kiosk (spec 008 US3). The Keycloak client is
/// created with the kiosk scope bundle + the
/// <c>/fabs/&lt;Fab&gt;</c> group attribute. <paramref name="Name"/> is the
/// caller-supplied part only — the handler composes the fab-qualified
/// clientId itself (<c>kiosk.{fab}.{name}</c>, ADR-0172), the same way
/// <c>RegisterDeviceCommandHandler</c> composes a device's.
/// </summary>
public sealed record EnrollKioskCommand(
    string Name,
    FabIdentifier Fab,
    OperatorIdentifier EnrolledBy)
    : ICommand<Result<KioskCredentialsDto, EnrollKioskError>>;
