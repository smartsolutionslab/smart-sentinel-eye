using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

/// <summary>
/// A provider that answers the enumeration with a fixed set of enrolled kiosks
/// and accepts every strip.
///
/// <para>
/// Hand-written rather than mocked (ADR-0054), and deliberately not
/// <c>Identity.Application.Tests</c>' <c>FakeKeycloakAdminClient</c> — that
/// assembly cannot be referenced from here without giving Application's tests a
/// path to Infrastructure, which is the absence spec 092 relies on when it calls
/// <c>Does_not_touch_an_account_this_system_did_not_enrol</c> vacuous.
/// </para>
/// </summary>
public sealed class EnrolledKiosksKeycloakAdminClient(params string[] kiosks) : IKeycloakAdminClient
{
    /// <summary>
    /// How many times the enumeration was asked for. Asserted, so "nothing was
    /// logged" cannot be satisfied by a pass that never ran.
    /// </summary>
    public int EnumerationAttempts { get; private set; }

    public List<string> Stripped { get; } = [];

    /// <summary>
    /// Kiosks whose service account holds no directly-assigned realm role, so
    /// the strip is reached and removes nothing — <c>HttpKeycloakAdminClient</c>'s
    /// <c>if (assigned.Length == 0) return;</c>, which is what a kiosk already
    /// swept on an earlier boot looks like.
    ///
    /// <para>
    /// Empty by default, so a kiosk named in the constructor still holds the
    /// privilege. That default is what keeps
    /// <c>A_pass_that_finds_a_kiosk_says_so_once_and_names_the_count</c> reading
    /// the same as it did before spec 132.
    /// </para>
    /// </summary>
    public HashSet<string> AlreadyStripped { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(CancellationToken cancellationToken)
    {
        EnumerationAttempts++;
        return Task.FromResult<IReadOnlyList<string>>(kiosks);
    }

    public Task StripInheritedRealmRolesAsync(string clientId, CancellationToken cancellationToken)
    {
        Stripped.Add(clientId);
        return Task.CompletedTask;
    }

    public Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken) => throw NotPartOfASweep();

    public Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken) => throw NotPartOfASweep();

    public Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken) => throw NotPartOfASweep();

    public Task DisableClientAsync(
        string clientId, CancellationToken cancellationToken) => throw NotPartOfASweep();

    public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken) => throw NotPartOfASweep();

    private static NotSupportedException NotPartOfASweep() =>
        new("A sweep enumerates kiosks and strips them; it calls nothing else.");
}
