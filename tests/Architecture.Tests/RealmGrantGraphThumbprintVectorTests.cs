namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 440 T162 — the thumbprint the realm-file facts compare a registered key's
/// <c>kid</c> against is only an oracle if it agrees with RFC 7638 itself. The vector is
/// the standard's own (§3.1), so the expectation is a fact about the world and not a
/// restatement of the implementation.
/// </summary>
public class RealmGrantGraphThumbprintVectorTests
{
    private const string Modulus =
        "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw";

    private const string Exponent = "AQAB";

    private const string Rfc7638Section31Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    [Fact]
    public void The_graphs_thumbprint_of_the_RFC_7638_section_3_1_key_is_the_published_one()
    {
        RealmGrantGraph.Thumbprint(Modulus, Exponent).ShouldBe(Rfc7638Section31Thumbprint);
    }
}
