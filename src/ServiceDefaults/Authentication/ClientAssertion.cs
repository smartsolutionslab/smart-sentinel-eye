using System.Security.Cryptography;

namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// STUB (spec 440 T060, red commit): throws until the backend slice (T080) writes it.
/// </summary>
internal static class ClientAssertion
{
    public static string Create(string clientIdentifier, string tokenEndpoint, RSA key, TimeProvider clock) =>
        throw new NotImplementedException();
}
