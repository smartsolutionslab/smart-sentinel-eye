using System.Security.Cryptography;
using System.Text;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// SHA-256 hash of a webhook integration's bearer token. We never
/// store the plaintext (spec 006 FR-023); the token is returned
/// once on registration and the caller is responsible for storing
/// it. <see cref="Matches"/> uses
/// <see cref="CryptographicOperations.FixedTimeEquals"/> for
/// constant-time compare.
/// </summary>
public sealed record BearerTokenHash : IValueObject<string>
{
    public string Value { get; }

    private BearerTokenHash(string base64Hash) => Value = base64Hash;

    /// <summary>Mints a fresh token + its hash. The plaintext is shown to the caller exactly once.</summary>
    public static (BearerTokenHash hash, string plaintext) Generate()
    {
        Span<byte> raw = stackalloc byte[32];
        RandomNumberGenerator.Fill(raw);
        string plaintext = Convert.ToBase64String(raw);
        return (FromPlaintext(plaintext), plaintext);
    }

    public static BearerTokenHash FromPlaintext(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return new BearerTokenHash(Convert.ToBase64String(hash));
    }

    public static BearerTokenHash FromStored(string base64Hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Hash);
        return new BearerTokenHash(base64Hash);
    }

    public bool Matches(string candidatePlaintext)
    {
        if (string.IsNullOrEmpty(candidatePlaintext)) return false;
        byte[] candidate = SHA256.HashData(Encoding.UTF8.GetBytes(candidatePlaintext));
        byte[] stored = Convert.FromBase64String(Value);
        return CryptographicOperations.FixedTimeEquals(candidate, stored);
    }

    public sealed override string ToString() => "<sha256-hash>";
}
