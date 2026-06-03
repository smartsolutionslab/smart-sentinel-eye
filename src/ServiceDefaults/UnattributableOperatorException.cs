namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Thrown by <see cref="ClaimsPrincipalExtensions.ToOperatorIdentifier"/>
/// when an authenticated request carries no usable <c>sub</c> claim, so the
/// action cannot be attributed to a real operator. Mapped to a
/// <c>401 OPERATOR_UNIDENTIFIED</c> by
/// <see cref="Authorization.UnattributableOperatorExceptionHandler"/>.
/// </summary>
public sealed class UnattributableOperatorException()
    : Exception("The authenticated principal carries no usable 'sub' claim; the operator cannot be identified.");
