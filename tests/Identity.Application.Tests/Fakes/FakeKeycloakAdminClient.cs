using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

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
    private readonly Dictionary<string, KeycloakClientRepresentation> clients =
        new(StringComparer.Ordinal);

    public List<string> Disabled { get; } = [];

    /// <summary>
    /// Kiosk accounts whose inherited realm privileges have been taken away.
    ///
    /// <para>
    /// A <b>set</b>, so a test can assert the removal is idempotent without
    /// counting: sweeping twice must not change what it holds.
    /// </para>
    ///
    /// <para>
    /// <b>Which is also why a set alone cannot see a sweep doing more work</b>
    /// (#2151): repeated calls collapse into it. Use <see cref="StripCalls"/>
    /// when the count is the claim.
    /// </para>
    /// </summary>
    public HashSet<string> Stripped { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Every <see cref="StripInheritedRealmRolesAsync"/> call, in order,
    /// repeats included — the record <see cref="Stripped"/> discards.
    ///
    /// <para>
    /// Enrolment's own strip is deliberately not recorded here: it happens
    /// inside <see cref="CreateClientAsync"/> and is not a call any sweep made.
    /// </para>
    /// </summary>
    public List<string> StripCalls { get; } = [];

    /// <summary>Client ids for which the strip should fail, however it is reached.</summary>
    public HashSet<string> StripFailsFor { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CurrentSecrets { get; } = new(StringComparer.Ordinal);

    public string? FailNextCall { get; set; }
    public int CallCount { get; private set; }

    /// <summary>
    /// Scoped to <see cref="DisableClientAsync"/> alone, unlike
    /// <see cref="FailNextCall"/> which the next call to <b>any</b> method
    /// consumes — #2628's race-path tests need <see cref="CreateClientAsync"/>
    /// to succeed and only the later, best-effort disable to fail. One-shot,
    /// same as <see cref="FailNextCall"/>.
    /// </summary>
    public Exception? FailNextDisableWith { get; set; }

    /// <summary>
    /// Clients <see cref="GetStampedClientsAsync"/> reports — independent of
    /// <see cref="Created"/>, the record <see cref="CreateClientAsync"/>
    /// builds. <c>OrphanedClientSweepTests</c> plants a stamped client
    /// directly, the way <c>RealmProbe</c> plants one against the real
    /// provider, rather than going through enrolment or registration.
    /// </summary>
    public List<StampedClient> StampedClients { get; } = [];

    /// <summary>
    /// When each stamped client's service-account user was created, keyed by
    /// client id. A client id absent here answers <c>None</c> —
    /// <c>OrphanedClientSweepTests</c>' U7, "no service account".
    /// </summary>
    public Dictionary<string, DateTimeOffset> ServiceAccountCreatedAt { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// What <see cref="GetServiceAccountCreatedAtAsync"/> should throw instead
    /// of answering, keyed by client id — U6 (a transport failure, so the
    /// other orphan is still disabled) and U11 (an
    /// <see cref="OperationCanceledException"/>, which must propagate rather
    /// than being swallowed into <c>Unreachable</c>).
    /// </summary>
    public Dictionary<string, Exception> ServiceAccountCreatedAtThrows { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// What <see cref="DisableClientAsync"/> should throw instead of
    /// disabling, keyed by client id — U6b, where the second orphan must
    /// still be disabled despite the first's failure. Distinct from
    /// <see cref="FailNextDisableWith"/>, which is one-shot against the next
    /// call from <b>any</b> client and already serves #2628's race-path
    /// tests: this is per-client and order-independent.
    /// </summary>
    public Dictionary<string, Exception> DisableFailsFor { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Every call this fake made that the sweep's tests care about the order
    /// of, in order — <c>OrphanedClientSweepTests</c>' U9 (Keycloak read
    /// before Postgres, spec 320 S5). Assign the same list to
    /// <c>InMemoryRegisteredClientRepository.CallLog</c> so one sequence
    /// spans both ports the sweep reads from.
    /// </summary>
    public List<string> CallLog { get; set; } = [];

    /// <summary>
    /// Every representation handed to <see cref="CreateClientAsync"/>, in order.
    ///
    /// <para>
    /// Recorded separately from the created-client map because this is what was
    /// <em>asked for</em>: a create whose strip fails removes the client again,
    /// and the request the handler composed is still the thing under test.
    /// <c>RuntimeClientAudienceTests</c> (spec 069) reads it — nothing else can
    /// see the scopes a runtime-created client is born with.
    /// </para>
    /// </summary>
    public List<KeycloakClientRepresentation> Created { get; } = [];

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

        Ensure.That(representation).IsNotNull();
        Created.Add(representation);
        if (clients.ContainsKey(representation.ClientId))
        {
            throw new KeycloakClientAlreadyExistsException(representation.ClientId);
        }
        clients.Add(representation.ClientId, representation);

        // **Production strips as part of creating, so this must too** (spec 052).
        // A fake that created an account and left the privilege on it would let
        // every test describe a system that does not exist — and the failure
        // path below is what proves an enrolment cannot report success over an
        // account still holding it.
        if (StripFailsFor.Contains(representation.ClientId))
        {
            // The real client removes the half-enrolled client before rethrowing,
            // so a retry is not blocked by a leftover.
            clients.Remove(representation.ClientId);
            throw new InvalidOperationException(
                $"Keycloak refused to strip '{representation.ClientId}'.");
        }
        Stripped.Add(representation.ClientId);

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

        if (!clients.ContainsKey(clientId))
        {
            throw new KeycloakClientNotFoundException(clientId);
        }
        string secret = $"secret-{clientId}-rotated";
        CurrentSecrets[clientId] = secret;
        return Task.FromResult(new KeycloakClientCredentials(secret));
    }

    /// <summary>
    /// Hands back the secret the client already has, without changing it — the
    /// distinction from <see cref="RotateClientSecretAsync"/> that ADR-0142's
    /// replay depends on. A fake that rotated here would let a broken replay
    /// pass, because the caller would still receive *a* working secret.
    /// </summary>
    public Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        if (!clients.ContainsKey(clientId))
        {
            throw new KeycloakClientNotFoundException(clientId);
        }

        return Task.FromResult(new KeycloakClientCredentials(CurrentSecrets[clientId]));
    }

    public Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        CallLog.Add(nameof(DisableClientAsync));
        if (DisableFailsFor.TryGetValue(clientId, out Exception? perClientFailure))
        {
            throw perClientFailure;
        }
        if (FailNextDisableWith is not null)
        {
            Exception toThrow = FailNextDisableWith;
            FailNextDisableWith = null;
            throw toThrow;
        }
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        Disabled.Add(clientId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Spec 320 plan §3 — one <c>GET /clients</c>, filtered to rows carrying
    /// <c>sse.kind</c>, the same request <see cref="GetEnrolledKioskClientIdsAsync"/>
    /// makes. The caller (<c>OrphanedClientSweep</c>) filters to the kinds it
    /// sweeps; this fake hands back every stamped client it was given,
    /// whatever its kind.
    /// </summary>
    public Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(
        CancellationToken cancellationToken)
    {
        CallCount++;
        CallLog.Add(nameof(GetStampedClientsAsync));
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        return Task.FromResult<IReadOnlyList<StampedClient>>([.. StampedClients]);
    }

    /// <summary>
    /// Spec 320 plan §3 — <c>None</c> when this fake was not told a
    /// creation time for <paramref name="clientId"/> (U7), unless a failure
    /// was asked for instead (U6, U11).
    /// </summary>
    public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
        string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        CallLog.Add(nameof(GetServiceAccountCreatedAtAsync));
        if (ServiceAccountCreatedAtThrows.TryGetValue(clientId, out Exception? toThrow))
        {
            throw toThrow;
        }
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        return Task.FromResult(
            ServiceAccountCreatedAt.TryGetValue(clientId, out DateTimeOffset createdAt)
                ? Option<DateTimeOffset>.Some(createdAt)
                : Option<DateTimeOffset>.None);
    }

    /// <summary>
    /// Sub-groups this fake will report, keyed by parent path. Spec 019 reads
    /// <c>/fabs</c> through this seam; Identity's own handlers never call it.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> SubGroups { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
        CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        // Mirrors production: the set is derived from the attribute enrolment
        // stamps, not from a naming convention repeated here.
        IReadOnlyList<string> kiosks = clients
            .Where(entry => entry.Value.Attributes is not null
                && entry.Value.Attributes.TryGetValue("sse.kind", out string? kind)
                && kind == "kiosk")
            .Select(entry => entry.Key)
            .ToArray();

        return Task.FromResult(kiosks);
    }

    /// <summary>
    /// Answers whether this call removed anything, which is
    /// <see cref="HashSet{T}.Add"/>'s own answer over <see cref="Stripped"/> —
    /// so the fake models the provider's idempotence exactly: the first strip of
    /// an account changes it, every later one finds nothing to remove and says
    /// so (spec 132, #2169).
    /// </summary>
    public Task<bool> StripInheritedRealmRolesAsync(
        string clientId, CancellationToken cancellationToken)
    {
        CallCount++;
        StripCalls.Add(clientId);
        if (StripFailsFor.Contains(clientId))
        {
            throw new InvalidOperationException($"Keycloak refused to strip '{clientId}'.");
        }

        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        return Task.FromResult(Stripped.Add(clientId));
    }

    public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken)
    {
        CallCount++;
        if (FailNextCall is not null)
        {
            ThrowAndClear();
        }

        // A path this fake's realm does not hold is absent, not childless
        // (#2139) — the dictionary miss is the fake's way of saying the group
        // is not there, which is the case the empty list used to swallow.
        return Task.FromResult(
            SubGroups.TryGetValue(parentPath, out IReadOnlyList<string>? names)
                ? Option<IReadOnlyList<string>>.Some(names)
                : Option<IReadOnlyList<string>>.None);
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
