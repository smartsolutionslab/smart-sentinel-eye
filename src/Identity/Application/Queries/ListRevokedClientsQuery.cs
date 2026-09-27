using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Backs <c>GET /registered-clients/revoked</c> (spec 270, ADR-0160 §2,
/// FR-007/FR-008): every disabled <see cref="Domain.RegisteredClient.RegisteredClient"/>,
/// across every fab and every <see cref="Domain.RegisteredClient.ClientKind"/>,
/// with the latest <c>DisabledAt</c> per client id.
///
/// <para>
/// Deliberately not fab-scoped, unlike <see cref="ListKiosksQuery"/> /
/// <see cref="ListDevicesQuery"/>: its one caller is the
/// <c>revocation-list-reader</c> platform service account, which belongs to
/// no fab group, and every consumer needs every fab (plan.md §4.2).
/// </para>
/// </summary>
public sealed record ListRevokedClientsQuery : IQuery<Result<IReadOnlyList<RevokedClientEntry>, ListClientsError>>;
