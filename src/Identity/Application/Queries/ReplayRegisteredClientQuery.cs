using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Recovers the state a credential-minting endpoint needs to rebuild the answer
/// it already gave, for a caller replaying its own idempotency key (ADR-0142).
///
/// <para>
/// One query for all three endpoints. What they return differs — a device adds
/// its type and identifier, a webhook rotation adds its integration name — but
/// every one of those extras comes from the replayed <i>request</i>, which a
/// transparent retry carries by definition. What has to be recovered is only
/// what the server holds, and that is the same four values in each case.
/// </para>
/// </summary>
/// <param name="Client">The registration recorded against the key.</param>
/// <param name="AnsweredAt">Spec 454: the version the original answer carried; <c>None</c> replays unbound.</param>
public sealed record ReplayRegisteredClientQuery(
    RegisteredClientIdentifier Client,
    Option<AggregateVersion> AnsweredAt)
{
    // Devices and kiosks stay unbound (spec 454 ruling 4).
    public ReplayRegisteredClientQuery(RegisteredClientIdentifier client)
        : this(client, Option<AggregateVersion>.None)
    {
    }
}
