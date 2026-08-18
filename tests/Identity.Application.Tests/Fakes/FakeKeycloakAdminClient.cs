using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Test-side <see cref="IKeycloakAdminClient"/> that mirrors the
/// production contract: Create rejects duplicates with
/// <see cref="KeycloakClientAlreadyExistsException"/>, Rotate +
/// Disable throw <see cref="KeycloakClientNotFoundException"/>
/// when the client is unknown. <see cref="FailNextCall"/> lets
/// tests inject a transport failure to exercise the
/// <c>KEYCLOAK_UNAVAILABLE</c> error path.
/// </summary>
public sealed class FakeKeycloakAdminClient : IKeycloakAdminClient
{
    private readonly Dictionary<string, KeycloakClientRepresentation> _clients =
        new(StringComparer.Ordinal);

    public List<string> Disabled { get; } = [];
    public Dictionary<string, string> CurrentSecrets { get; } = new(StringComparer.Ordinal);

    public string? FailNextCall { get; set; }
    public int CallCount { get; private set; }

    public Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        ArgumentNullException.ThrowIfNull(representation);
        if (_clients.ContainsKey(representation.ClientId))
        {
            throw new KeycloakClientAlreadyExistsException(representation.ClientId);
        }
        _clients.Add(representation.ClientId, representation);
        string secret = $"secret-{representation.ClientId}";
        CurrentSecrets[representation.ClientId] = secret;
        return Task.FromResult(new KeycloakClientCredentials(secret));
    }

    public Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        if (!_clients.ContainsKey(clientId))
        {
            throw new KeycloakClientNotFoundException(clientId);
        }
        string secret = $"secret-{clientId}-rotated";
        CurrentSecrets[clientId] = secret;
        return Task.FromResult(new KeycloakClientCredentials(secret));
    }

    public Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        Disabled.Add(clientId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sub-groups this fake will report, keyed by parent path. Spec 019 reads
    /// <c>/fabs</c> through this seam; Identity's own handlers never call it.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> SubGroups { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        return Task.FromResult(
            SubGroups.TryGetValue(parentPath, out IReadOnlyList<string>? names) ? names : []);
    }

    private void ThrowAndClear()
    {
        string message = FailNextCall!;
        FailNextCall = null;
        // Surface as HttpRequestException so the handlers' generic
        // catch-all (not OperationCanceledException) treats it as
        // a transport failure rather than a domain invariant
        // violation.
        throw new HttpRequestException(message);
    }
}
