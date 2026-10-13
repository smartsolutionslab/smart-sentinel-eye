using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.WebhookIntegration;

public class BearerTokenHashTests
{
    [Fact]
    public void Generate_returns_a_plaintext_and_a_hash_that_matches_it()
    {
        (var hash, var plaintext) = BearerTokenHash.Generate();

        hash.Matches(plaintext).ShouldBeTrue();
        hash.Value.ShouldNotBeEmpty();
        plaintext.ShouldNotBeEmpty();
    }

    [Fact]
    public void Matches_rejects_the_wrong_plaintext()
    {
        (var hash, var _) = BearerTokenHash.Generate();
        hash.Matches("not-the-token").ShouldBeFalse();
        hash.Matches(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void FromPlaintext_is_deterministic_for_the_same_input()
    {
        var a = BearerTokenHash.FromPlaintext("secret");
        var b = BearerTokenHash.FromPlaintext("secret");
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void FromStored_round_trips_a_known_hash_value()
    {
        var original = BearerTokenHash.FromPlaintext("secret");
        var restored = BearerTokenHash.FromStored(original.Value);
        restored.Value.ShouldBe(original.Value);
        restored.Matches("secret").ShouldBeTrue();
    }

    [Fact]
    public void ToString_does_not_leak_the_hash()
    {
        var hash = BearerTokenHash.FromPlaintext("secret");
        hash.ToString().ShouldNotContain(hash.Value);
    }
}
