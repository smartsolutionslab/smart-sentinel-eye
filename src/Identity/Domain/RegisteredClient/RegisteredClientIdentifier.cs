using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Stable, sortable identifier for the local audit row that maps
/// a Keycloak client (the system of record) to an Identity-side
/// registry entry (ADR-0090). Distinct from the Keycloak
/// <c>clientId</c> string (carried as <see cref="ClientId"/>) so
/// the local Guid v7 remains stable even if a Keycloak client is
/// re-created with the same client-id string.
/// </summary>
public readonly record struct RegisteredClientIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static RegisteredClientIdentifier New() => new(Guid.CreateVersion7());

    public static RegisteredClientIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("RegisteredClientIdentifier cannot be empty.", nameof(value))
            : new RegisteredClientIdentifier(value);

    public override string ToString() => Value.ToString();
}
