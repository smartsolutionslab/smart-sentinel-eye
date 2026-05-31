using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal UserWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void Returns_the_operator_from_the_sub_claim()
    {
        Guid subject = Guid.CreateVersion7();

        OperatorIdentifier actingOperator = UserWith(new Claim("sub", subject.ToString())).ToOperatorIdentifier();

        actingOperator.Value.ShouldBe(subject);
    }

    [Fact]
    public void Falls_back_to_the_name_identifier_claim_when_sub_is_absent()
    {
        Guid identifier = Guid.CreateVersion7();

        OperatorIdentifier actingOperator =
            UserWith(new Claim(ClaimTypes.NameIdentifier, identifier.ToString())).ToOperatorIdentifier();

        actingOperator.Value.ShouldBe(identifier);
    }

    [Fact]
    public void Fails_closed_when_no_usable_subject_claim_is_present()
    {
        ClaimsPrincipal user = UserWith(new Claim("scope", "sse.overlays.write"));

        Should.Throw<UnattributableOperatorException>(() => user.ToOperatorIdentifier());
    }

    [Fact]
    public void Fails_closed_when_the_subject_claim_is_not_a_guid()
    {
        ClaimsPrincipal user = UserWith(new Claim("sub", "not-a-guid"));

        Should.Throw<UnattributableOperatorException>(() => user.ToOperatorIdentifier());
    }
}
