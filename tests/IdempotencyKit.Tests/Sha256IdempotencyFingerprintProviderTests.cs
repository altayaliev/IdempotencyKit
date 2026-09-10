using System.Text;

namespace IdempotencyKit.Tests;

public class Sha256IdempotencyFingerprintProviderTests
{
    private readonly Sha256IdempotencyFingerprintProvider _provider = new();

    [Fact]
    public void Same_inputs_always_produce_the_same_fingerprint()
    {
        var body = Encoding.UTF8.GetBytes("{\"sku\":\"abc\"}");

        var first = _provider.Compute("POST", "/orders", body);
        var second = _provider.Compute("POST", "/orders", body);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("PUT", "/orders", "{\"sku\":\"abc\"}")]      // different method
    [InlineData("POST", "/orders/1", "{\"sku\":\"abc\"}")]   // different path
    [InlineData("POST", "/orders", "{\"sku\":\"xyz\"}")]     // different body
    public void Changing_any_component_changes_the_fingerprint(string method, string path, string body)
    {
        var baseline = _provider.Compute("POST", "/orders", Encoding.UTF8.GetBytes("{\"sku\":\"abc\"}"));
        var changed = _provider.Compute(method, path, Encoding.UTF8.GetBytes(body));

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Fingerprint_is_a_64_character_uppercase_hex_sha256_digest()
    {
        var fingerprint = _provider.Compute("POST", "/orders", Encoding.UTF8.GetBytes("{}"));

        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9A-F]{64}$", fingerprint);
    }

    [Fact]
    public void Empty_body_is_handled_without_error()
    {
        var fingerprint = _provider.Compute("GET", "/health", ReadOnlySpan<byte>.Empty);

        Assert.Equal(64, fingerprint.Length);
    }

    [Fact]
    public void Method_and_path_boundary_is_not_ambiguous()
    {
        // Without a separator, ("POS", "T/orders") and ("POST", "/orders") could collide.
        // The provider must not conflate these.
        var a = _provider.Compute("POS", "T/orders", ReadOnlySpan<byte>.Empty);
        var b = _provider.Compute("POST", "/orders", ReadOnlySpan<byte>.Empty);

        Assert.NotEqual(a, b);
    }
}
