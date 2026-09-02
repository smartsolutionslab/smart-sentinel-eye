using System.Security.Cryptography;
using System.Text;
using SmartSentinelEye.Shared.Kernel.Primitives;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Keycloak client secret (spec 008). **Write-once** —
/// <see cref="Reveal"/> returns the plaintext exactly once and
/// throws on every subsequent call. <see cref="ToString"/>
/// redacts. Equality is on the SHA-256 hash so two instances
/// carrying the same plaintext compare equal without the
/// plaintext leaking into the hash code or equality probes.
///
/// <para>
/// We never persist the plaintext — Keycloak is the system of
/// record. <see cref="ClientSecret"/> is a transient transport
/// VO returned from <see cref="RegisteredClient.Register"/> /
/// <see cref="RegisteredClient.Rotate"/> exactly once.
/// </para>
/// </summary>
public sealed class ClientSecret : IValueObject<string>, IEquatable<ClientSecret>
{
    private readonly string hash;
    private string? plaintext;
    private bool revealed;
    private readonly object gate = new();

    public string Value => hash;

    private ClientSecret(string plaintext, string hash)
    {
        this.plaintext = plaintext;
        this.hash = hash;
    }

    public static ClientSecret WrapPlaintext(string plaintext)
    {
        Ensure.That(plaintext).IsNotNull().IsNotNullOrWhiteSpace();
        return new ClientSecret(plaintext, HashOf(plaintext));
    }

    /// <summary>
    /// Returns the plaintext secret. Throws on the second call;
    /// the caller is expected to hand it to the HTTP response and
    /// discard the reference.
    /// </summary>
    public string Reveal()
    {
        lock (gate)
        {
            if (revealed || plaintext is null)
            {
                throw new InvalidOperationException(
                    "ClientSecret can be revealed exactly once.");
            }
            string secret = plaintext;
            plaintext = null;
            revealed = true;
            return secret;
        }
    }

    public bool Equals(ClientSecret? other) =>
        other is not null && string.Equals(hash, other.hash, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ClientSecret);

    public override int GetHashCode() => hash.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => "<redacted>";

    private static string HashOf(string plaintext)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(bytes);
    }
}
