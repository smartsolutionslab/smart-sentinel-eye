using System.Buffers.Text;
using System.Security.Cryptography;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authentication;

/// <summary>
/// Spec 440 T162 — <see cref="AssertionInspector.Thumbprint"/> is the oracle
/// <c>ClientAssertionTests</c> holds the assertion's <c>kid</c> to. An oracle that has never
/// been compared with the standard is the code under test's own opinion, so it is held to
/// RFC 7638 §3.1's published key and thumbprint.
/// </summary>
public class AssertionThumbprintVectorTests
{
    private const string Modulus =
        "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw";

    private const string Exponent = "AQAB";

    private const string Rfc7638Section31Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    [Fact]
    public void The_inspectors_thumbprint_of_the_RFC_7638_section_3_1_key_is_the_published_one()
    {
        using var key = RSA.Create();
        key.ImportParameters(new RSAParameters
        {
            Modulus = Base64Url.DecodeFromChars(Modulus),
            Exponent = Base64Url.DecodeFromChars(Exponent),
        });

        AssertionInspector.Thumbprint(key).ShouldBe(Rfc7638Section31Thumbprint);
    }
}
