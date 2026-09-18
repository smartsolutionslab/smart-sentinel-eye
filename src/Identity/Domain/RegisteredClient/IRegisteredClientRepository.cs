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

    /// <summary>
    /// Scoped to <paramref name="fab"/> in the predicate itself, mirroring
    /// <c>ICameraRepository.GetWithinFabAsync</c> (ADR-0091): a client
    /// enrolled in a different fab is never materialised, so a caller cannot
    /// disable it by naming a fab it does not belong to. Also excludes
    /// Disabled rows, matching <see cref="GetByClientIdAsync"/>.
    /// </summary>
    Task<Option<RegisteredClient>> GetWithinFabAsync(
        ClientId clientId, FabIdentifier fab, CancellationToken cancellationToken);

    void Add(RegisteredClient client);

    Task SaveAsync(CancellationToken cancellationToken);
}
