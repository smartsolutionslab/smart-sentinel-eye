using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class ClientSecretTests
{
    [Fact]
    public void Reveal_returns_the_plaintext_exactly_once()
    {
        var secret = ClientSecret.WrapPlaintext("super-secret");

        secret.Reveal().ShouldBe("super-secret");

        Should.Throw<InvalidOperationException>(() => secret.Reveal());
    }

    [Fact]
    public void ToString_does_not_leak_the_plaintext_or_the_hash()
    {
        var secret = ClientSecret.WrapPlaintext("super-secret");
        secret.ToString().ShouldBe("<redacted>");
        secret.ToString().ShouldNotContain("super-secret");
        secret.ToString().ShouldNotContain(secret.Value);
    }

    [Fact]
    public void Equality_is_on_the_hash_so_two_instances_with_the_same_plaintext_compare_equal()
    {
        var a = ClientSecret.WrapPlaintext("super-secret");
        var b = ClientSecret.WrapPlaintext("super-secret");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_plaintexts_are_not_equal()
    {
        var a = ClientSecret.WrapPlaintext("one");
        var b = ClientSecret.WrapPlaintext("two");

        a.ShouldNotBe(b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WrapPlaintext_rejects_empty_input(string raw)
    {
        Action act = () => ClientSecret.WrapPlaintext(raw);
        act.ShouldThrow<ArgumentException>();
    }
}
