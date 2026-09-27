using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Fetches the current, whole list of disabled clients (spec 270, ADR-0160
/// §2). Two implementations: <see cref="HttpRevokedClientSource"/>, the
/// default for every non-Identity service, and Identity's own
/// <c>LocalRevokedClientSource</c>, which runs the same query handler in
/// process rather than calling itself over HTTP (plan.md §3).
/// </summary>
public interface IRevokedClientSource
{
    Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken);
}
