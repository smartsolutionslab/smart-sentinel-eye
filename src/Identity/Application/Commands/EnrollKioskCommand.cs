using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands;

/// <summary>
/// Enrolls a new kiosk (spec 008 US3). The Keycloak client is
/// created with the kiosk scope bundle + the
/// <c>/fabs/&lt;Fab&gt;</c> group attribute.
/// </summary>
public sealed record EnrollKioskCommand(
    ClientId ClientId,
    FabIdentifier Fab,
    OperatorIdentifier EnrolledBy)
    : ICommand<Result<KioskCredentialsDto, EnrollKioskError>>;
