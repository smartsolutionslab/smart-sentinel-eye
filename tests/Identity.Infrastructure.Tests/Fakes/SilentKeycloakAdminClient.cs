using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

/// <summary>
/// A provider whose enumeration call is black-holed — a socket that never
/// answers and never resets, honouring only cancellation, exactly as
/// <c>HttpClient</c> does against a TEST-NET-3 target (spec 317 §1.2).
///
/// <para>
/// Hand-written rather than mocked (ADR-0054). Deliberately distinct from
/// <see cref="UnreachableKeycloakAdminClient"/> — that one fails fast (a
/// refused connection), and spec 317's red needs a call that never completes
/// on its own, so the only way out is the bound.
/// </para>
/// </summary>
public sealed class SilentKeycloakAdminClient : IKeycloakAdminClient
{
    /// <summary>
    /// How many times the enumeration was attempted. Asserted, so a test
    /// cannot be satisfied by a hosted service that gave up without ever
    /// calling the provider at all.
    /// </summary>
    public int EnumerationAttempts { get; private set; }

    /// <summary>
    /// How many times <see cref="GetStampedClientsAsync"/> — the first call
    /// <c>OrphanedClientSweep.SweepAsync</c> makes (spec 320 plan §4, S5) —
    /// was attempted. The sweep's own black hole, mirroring
    /// <see cref="EnumerationAttempts"/> for <c>KioskPrivilegeSweep</c>.
    /// </summary>
    public int StampedClientAttempts { get; private set; }

    public Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken) => throw Unreachable();

    public Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken) => throw Unreachable();

    public Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken) => throw Unreachable();

    public Task DisableClientAsync(
        string clientId, CancellationToken cancellationToken) => throw Unreachable();

    public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken) => throw Unreachable();

    public async Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
        CancellationToken cancellationToken)
    {
        EnumerationAttempts++;

        // The black hole: never completes on its own. HttpClient's own
        // behaviour against a target that never sends a SYN-ACK — the only
        // thing that ends this await is cancellation, exactly as the bound
        // (or host shutdown) must supply.
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        // Unreachable, but keeps the compiler honest about the return type.
        throw Unreachable();
    }

    public Task<bool> StripInheritedRealmRolesAsync(
        string clientId, CancellationToken cancellationToken) => throw Unreachable();

    public async Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(
        CancellationToken cancellationToken)
    {
        StampedClientAttempts++;

        // The black hole, same shape as GetEnrolledKioskClientIdsAsync above:
        // never completes on its own. The only thing that ends this await is
        // cancellation, exactly as the hosted service's StartAsync (spec 317's
        // lesson) must supply for its own pass.
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw Unreachable();
    }

    public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
        string clientId, CancellationToken cancellationToken) => throw Unreachable();

    private static HttpRequestException Unreachable() =>
        new("Keycloak is not reachable.");
}
