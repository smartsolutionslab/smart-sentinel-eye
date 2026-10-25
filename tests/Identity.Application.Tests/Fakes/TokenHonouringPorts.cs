using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

// Spec 434. The in-memory fakes ignore their CancellationToken, so a handler
// that keeps using a cancelled request token passes against them. Every
// collaborator the post-commit compensation touches is wrapped here so it
// throws OperationCanceledException on a cancelled token, as EF Core, Npgsql
// and HttpClient do. The existing fakes are wrapped, not changed.

/// <summary>
/// Honours the token on every call and cancels <paramref name="request"/> at
/// the chosen point of the create branch.
/// </summary>
public sealed class TokenHonouringRegisteredClientRepository(
    IRegisteredClientRepository inner,
    CancellationTokenSource request,
    CancelPoint cancelAt) : IRegisteredClientRepository
{
    private int saveCalls;

    /// <summary>
    /// When set, that numbered <c>SaveAsync</c> call waits on its token alone,
    /// like a database that accepted the command and went silent.
    /// </summary>
    public int SaveNeverAnswersOnCall { get; set; }

    public Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetByIdentifierAsync(identifier, cancellationToken);
    }

    public Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetByClientIdAsync(clientId, cancellationToken);
    }

    public Task<Option<RegisteredClientAggregate>> GetWithinFabAsync(
        FabIdentifier fab, ClientId clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetWithinFabAsync(fab, clientId, cancellationToken);
    }

    public void Add(RegisteredClientAggregate client)
    {
        inner.Add(client);
        if (cancelAt == CancelPoint.BeforeFirstSave)
        {
            request.Cancel();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        saveCalls++;
        if (saveCalls == SaveNeverAnswersOnCall)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        await inner.SaveAsync(cancellationToken);
        if (cancelAt == CancelPoint.AfterFirstSave)
        {
            cancelAt = CancelPoint.Never;
            await request.CancelAsync();
        }
    }

    public Task<Option<AggregateVersion>> GetVersionAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken) =>
        inner.GetVersionAsync(identifier, cancellationToken);

    public Task<IReadOnlySet<ClientId>> GetActiveClientIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetActiveClientIdsAsync(cancellationToken);
    }
}

public enum CancelPoint
{
    Never,
    BeforeFirstSave,
    AfterFirstSave,
}

/// <summary>
/// Honours the token on every call. With <see cref="DisableNeverAnswers"/> set,
/// <c>DisableClientAsync</c> waits on the token alone, like a Keycloak that
/// accepted the connection and went silent.
/// </summary>
public sealed class TokenHonouringKeycloakAdminClient(IKeycloakAdminClient inner) : IKeycloakAdminClient
{
    public bool DisableNeverAnswers { get; set; }

    public Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.CreateClientAsync(representation, fabGroupPath, cancellationToken);
    }

    public Task<KeycloakClientCredentials> RotateClientSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.RotateClientSecretAsync(clientId, cancellationToken);
    }

    public Task<KeycloakClientCredentials> ReadClientSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.ReadClientSecretAsync(clientId, cancellationToken);
    }

    public async Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DisableNeverAnswers)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        await inner.DisableClientAsync(clientId, cancellationToken);
    }

    public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetSubGroupNamesAsync(parentPath, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetEnrolledKioskClientIdsAsync(cancellationToken);
    }

    public Task<bool> StripInheritedRealmRolesAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.StripInheritedRealmRolesAsync(clientId, cancellationToken);
    }

    public Task RemoveRealmDefaultPrivilegeAsync(string privilege, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.RemoveRealmDefaultPrivilegeAsync(privilege, cancellationToken);
    }

    public Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetStampedClientsAsync(cancellationToken);
    }

    public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
        string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetServiceAccountCreatedAtAsync(clientId, cancellationToken);
    }
}

public sealed class TokenHonouringWebhookIntegrationStatusLookup(IWebhookIntegrationStatusLookup inner)
    : IWebhookIntegrationStatusLookup
{
    public Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.GetStatusAsync(fab, integrationName, cancellationToken);
    }
}

public sealed class TokenHonouringEventBus(IEventBus inner) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.PublishAsync(integrationEvent, cancellationToken);
    }
}

public sealed class TokenHonouringTransactionalCommit(ITransactionalCommit inner) : ITransactionalCommit
{
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return inner.CommitAsync(cancellationToken);
    }
}
