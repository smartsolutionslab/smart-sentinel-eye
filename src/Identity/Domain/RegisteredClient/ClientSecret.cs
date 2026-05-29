using System.Security.Cryptography;
using System.Text;
using SmartSentinelEye.Shared.Kernel.Primitives;

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
    private readonly string _hash;
    private string? _plaintext;
    private bool _revealed;
    private readonly object _gate = new();

    public string Value => _hash;

    private ClientSecret(string plaintext, string hash)
    {
        _plaintext = plaintext;
        _hash = hash;
    }

    public static ClientSecret WrapPlaintext(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return new ClientSecret(plaintext, HashOf(plaintext));
    }

    /// <summary>
    /// Returns the plaintext secret. Throws on the second call;
    /// the caller is expected to hand it to the HTTP response and
    /// discard the reference.
    /// </summary>
    public string Reveal()
    {
        lock (_gate)
        {
            if (_revealed || _plaintext is null)
            {
                throw new InvalidOperationException(
                    "ClientSecret can be revealed exactly once.");
            }
            string plaintext = _plaintext;
            _plaintext = null;
            _revealed = true;
            return plaintext;
        }
    }

    public bool Equals(ClientSecret? other) =>
        other is not null && string.Equals(_hash, other._hash, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ClientSecret);

    public override int GetHashCode() => _hash.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => "<redacted>";

    private static string HashOf(string plaintext)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(bytes);
    }
}
