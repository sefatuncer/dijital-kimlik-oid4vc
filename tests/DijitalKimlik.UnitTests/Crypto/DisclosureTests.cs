using DijitalKimlik.Crypto.SelectiveDisclosure;

namespace DijitalKimlik.UnitTests.Crypto;

public class DisclosureTests
{
    // RFC 9901 §4.2.1, §4.2.2 ve §5.1'deki disclosure'lar ve SHA-256 özetleri.
    [Theory]
    [InlineData("WyJfMjZiYzRMVC1hYzZxMktJNmNCVzVlcyIsICJmYW1pbHlfbmFtZSIsICJNw7ZiaXVzIl0", "X9yH0Ajrdm1Oij4tWso9UzzKJvPoDxwmuEcO3XAdRC0")]
    [InlineData("WyJsa2x4RjVqTVlsR1RQVW92TU5JdkNBIiwgIkZSIl0", "w0I8EKcdCtUPkGCNUrfwVp2xEgNjtoIDlOxc9-PlOhs")]
    [InlineData("WyIyR0xDNDJzS1F2ZUNmR2ZyeU5STjl3IiwgImdpdmVuX25hbWUiLCAiSm9obiJd", "jsu9yVulwQQlhFlM_3JlzMaSFzglhQG0DpfayQwLUK4")]
    [InlineData("WyJlbHVWNU9nM2dTTklJOEVZbnN4QV9BIiwgImZhbWlseV9uYW1lIiwgIkRvZSJd", "TGf4oLbgwd5JQaHyKVQZU9UdGE0w5rtDsrZzfUaomLo")]
    [InlineData("WyJBSngtMDk1VlBycFR0TjRRTU9xUk9BIiwgImFkZHJlc3MiLCB7InN0cmVldF9hZGRyZXNzIjogIjEyMyBNYWluIFN0IiwgImxvY2FsaXR5IjogIkFueXRvd24iLCAicmVnaW9uIjogIkFueXN0YXRlIiwgImNvdW50cnkiOiAiVVMifV0", "XzFrzwscM6Gn6CJDc6vVK8BkMnfG8vOSKfpPIZdAfdE")]
    [InlineData("WyJsa2x4RjVqTVlsR1RQVW92TU5JdkNBIiwgIlVTIl0", "pFndjkZ_VCzmyTa6UjlZo3dh-ko8aIKQc9DlGzhaVYo")]
    [InlineData("WyJuUHVvUW5rUkZxM0JJZUFtN0FuWEZBIiwgIkRFIl0", "7Cf6JkPudry3lcbwHgeZ8khAv1U1OSlerP0VkBJrWZ0")]
    public void Digest_matches_rfc_9901(string encoded, string digest)
    {
        Assert.Equal(digest, Disclosure.Parse(encoded).Digest);
    }

    [Fact]
    public void Object_and_array_disclosures_are_parsed()
    {
        var property = Disclosure.Parse("WyJfMjZiYzRMVC1hYzZxMktJNmNCVzVlcyIsICJmYW1pbHlfbmFtZSIsICJNw7ZiaXVzIl0");
        var element = Disclosure.Parse("WyJsa2x4RjVqTVlsR1RQVW92TU5JdkNBIiwgIkZSIl0");

        Assert.Equal("family_name", property.Name);
        Assert.Equal("Möbius", property.Value!.GetValue<string>());
        Assert.Null(element.Name);
        Assert.Equal("FR", element.Value!.GetValue<string>());
    }

    [Fact]
    public void Digest_is_over_the_encoded_text_not_the_json()
    {
        // Aynı JSON dizisi boşluksuz yazılınca disclosure da özeti de değişir (RFC 9901 §4.2.1).
        var spaced = Disclosure.Parse("WyJfMjZiYzRMVC1hYzZxMktJNmNCVzVlcyIsICJmYW1pbHlfbmFtZSIsICJNw7ZiaXVzIl0");
        var compact = Disclosure.Parse("WyJfMjZiYzRMVC1hYzZxMktJNmNCVzVlcyIsImZhbWlseV9uYW1lIiwiTcO2Yml1cyJd");

        Assert.Equal(spaced.Name, compact.Name);
        Assert.NotEqual(spaced.Digest, compact.Digest);
    }

    [Fact]
    public void New_disclosures_get_a_fresh_128_bit_salt()
    {
        var first = Disclosure.ForProperty("given_name", "Amina");
        var second = Disclosure.ForProperty("given_name", "Amina");

        Assert.Equal(22, first.Salt.Length);
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Digest, second.Digest);
    }

    [Theory]
    [InlineData("_sd")]
    [InlineData("...")]
    public void Reserved_names_cannot_be_disclosed(string name)
    {
        Assert.Throws<ArgumentException>(() => Disclosure.ForProperty(name, "x"));
    }
}
