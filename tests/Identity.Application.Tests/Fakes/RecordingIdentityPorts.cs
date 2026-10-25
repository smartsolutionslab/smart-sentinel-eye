using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

// Decorators over the existing fakes that append one word per port call to a
// single shared, ordered list. The handlers' characterisation tests (spec 348)
// assert that list verbatim: a refactor that reorders a call across ports --
// status lookup, fab-scoped lookup, save, Keycloak, publish, commit -- cannot
// pass unnoticed, which no existing fake can express because each keeps its
// own private record. Everything is delegated; nothing here decides behaviour.

/// <summary>Words the decorators below write into the shared call log.</summary>
public static class Calls
{
    public const string Status = "status";
    public const string Lookup = "lookup";
    public const string Add = "add";
    public const string Save = "save";
    public const string Create = "create";
    public const string RotateSecret = "rotate-secret";
    public const string DisableClient = "disable-client";
    public const string Publish = "publish";
    public const string Commit = "commit";
}

public sealed class RecordingRegisteredClientRepository(
    IRegisteredClientRepository inner,
    List<string> log) : IRegisteredClientRepository
{
    public Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
    {
        log.Add("get-by-identifier");
        return inner.GetByIdentifierAsync(identifier, cancellationToken);
    }

    public Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
        ClientId clientId, CancellationToken cancellationToken)
    {
        log.Add("get-by-client-id");
        return inner.GetByClientIdAsync(clientId, cancellationToken);
    }

    public Task<Option<RegisteredClientAggregate>> GetWithinFabAsync(
        FabIdentifier fab, ClientId clientId, CancellationToken cancellationToken)
    {
        log.Add(Calls.Lookup);
        return inner.GetWithinFabAsync(fab, clientId, cancellationToken);
    }

    public void Add(RegisteredClientAggregate client)
    {
        log.Add(Calls.Add);
        inner.Add(client);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        log.Add(Calls.Save);
        return inner.SaveAsync(cancellationToken);
    }

    public Task<Option<AggregateVersion>> GetVersionAsync(
        RegisteredClientIdentifier identifier, CancellationToken cancellationToken) =>
        inner.GetVersionAsync(identifier, cancellationToken);

    public Task<IReadOnlySet<ClientId>> GetActiveClientIdsAsync(CancellationToken cancellationToken)
    {
        log.Add("active");
        return inner.GetActiveClientIdsAsync(cancellationToken);
    }
}

/// <summary>
/// Records the call and what it was called with (the fab group path
/// <c>FakeKeycloakAdminClient</c> never keeps), and can be told to throw
/// instead of delegating, so a test can pick the exception type a Keycloak
/// call fails with.
/// </summary>
public sealed class RecordingKeycloakAdminClient(
    IKeycloakAdminClient inner,
    List<string> log) : IKeycloakAdminClient
{
    public List<(KeycloakClientRepresentation Representation, string FabGroupPath)> CreateRequests { get; } = [];

    public List<string> RotateRequests { get; } = [];

    public Exception? CreateThrows { get; set; }

    public Exception? RotateThrows { get; set; }

    public Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken)
    {
        log.Add(Calls.Create);
        CreateRequests.Add((representation, fabGroupPath));
        if (CreateThrows is not null)
        {
            throw CreateThrows;
        }

        return inner.CreateClientAsync(representation, fabGroupPath, cancellationToken);
    }

    public Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        log.Add(Calls.RotateSecret);
        RotateRequests.Add(clientId);
        if (RotateThrows is not null)
        {
            throw RotateThrows;
        }

        return inner.RotateClientSecretAsync(clientId, cancellationToken);
    }

    public Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        log.Add("read-secret");
        return inner.ReadClientSecretAsync(clientId, cancellationToken);
    }

    public Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        log.Add(Calls.DisableClient);
        return inner.DisableClientAsync(clientId, cancellationToken);
    }

    public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken) =>
        inner.GetSubGroupNamesAsync(parentPath, cancellationToken);

    public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
        CancellationToken cancellationToken) =>
        inner.GetEnrolledKioskClientIdsAsync(cancellationToken);

    public Task<bool> StripInheritedRealmRolesAsync(
        string clientId, CancellationToken cancellationToken) =>
        inner.StripInheritedRealmRolesAsync(clientId, cancellationToken);

    public Task RemoveRealmDefaultPrivilegeAsync(
        string privilege, CancellationToken cancellationToken) =>
        inner.RemoveRealmDefaultPrivilegeAsync(privilege, cancellationToken);

    public Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(
        CancellationToken cancellationToken) =>
        inner.GetStampedClientsAsync(cancellationToken);

    public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
        string clientId, CancellationToken cancellationToken) =>
        inner.GetServiceAccountCreatedAtAsync(clientId, cancellationToken);
}

public sealed class RecordingWebhookIntegrationStatusLookup(
    IWebhookIntegrationStatusLookup inner,
    List<string> log) : IWebhookIntegrationStatusLookup
{
    public Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
    {
        log.Add(Calls.Status);
        return inner.GetStatusAsync(fab, integrationName, cancellationToken);
    }
}

public sealed class RecordingEventBus(IEventBus inner, List<string> log) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        log.Add(Calls.Publish);
        return inner.PublishAsync(integrationEvent, cancellationToken);
    }
}

public sealed class RecordingTransactionalCommit(ITransactionalCommit inner, List<string> log)
    : ITransactionalCommit
{
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        log.Add(Calls.Commit);
        return inner.CommitAsync(cancellationToken);
    }
}
