using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

/// <summary>
/// Compares the Basic-auth password MediaMTX forwards for an <c>api</c> hook
/// call against the configured <see cref="MediaMtxApiOptions.Secret"/>.
///
/// <para>
/// Both sides are SHA-256 hashed before <see cref="CryptographicOperations.FixedTimeEquals"/>
/// runs: that comparison requires equal-length spans, and hashing first means
/// a caller cannot learn the secret's length from the branch a differently-sized
/// guess would otherwise take before the fixed-time comparison even starts.
/// </para>
/// </summary>
public sealed class MediaMtxApiAuthenticator(IOptions<MediaMtxApiOptions> options) : IMediaMtxApiAuthenticator
{
    public bool Authorizes(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        string secret = options.Value.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        byte[] presented = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        byte[] configured = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

        return CryptographicOperations.FixedTimeEquals(presented, configured);
    }
}
