using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class RegisteredClientIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_identifier()
    {
        RegisteredClientIdentifier id = RegisteredClientIdentifier.New();
        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => RegisteredClientIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        Guid guid = Guid.CreateVersion7();
        RegisteredClientIdentifier.From(guid).Value.ShouldBe(guid);
    }
}
