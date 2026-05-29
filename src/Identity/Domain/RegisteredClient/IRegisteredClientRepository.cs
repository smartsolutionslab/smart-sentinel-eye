using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Registered-client repository contract (ADR-0041).
/// <see cref="GetByClientIdAsync"/> ignores Disabled rows so a
/// re-registration after disable returns <c>None</c> and the
/// caller can mint a fresh Keycloak client.
/// </summary>
public interface IRegisteredClientRepository
{
    Task<Option<RegisteredClient>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken);

    Task<Option<RegisteredClient>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken);

    void Add(RegisteredClient client);

    Task SaveAsync(CancellationToken cancellationToken);
}
