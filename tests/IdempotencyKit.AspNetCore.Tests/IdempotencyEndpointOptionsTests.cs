using System.Net;

namespace IdempotencyKit.AspNetCore.Tests;

/// <summary>
/// Verifies that <see cref="IdempotencyOptionsAttribute"/> (applied here via
/// <see cref="IdempotencyEndpointConventionBuilderExtensions.WithIdempotencyOptions"/> on
/// <c>/strict-orders</c> and <c>/small-body-orders</c> in <see cref="TestHost"/>) overrides the
/// app-wide <see cref="IdempotencyOptions"/> for just that endpoint, leaving other endpoints
/// (like <c>/orders</c>) on the app-wide defaults.
/// </summary>
public class IdempotencyEndpointOptionsTests
{
    [Fact]
    public async Task RequireHeaderOnMutatingRequests_override_applies_only_to_that_endpoint()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        // App-wide default is false, so /orders still allows a missing header.
        var ordersWithoutKey = await client.PostAsync("/orders", new StringContent("{\"sku\":\"x\"}"));
        Assert.Equal(HttpStatusCode.Created, ordersWithoutKey.StatusCode);

        // /strict-orders overrides RequireHeaderOnMutatingRequests to true for itself only.
        var strictWithoutKey = await client.PostAsync("/strict-orders", new StringContent("{\"sku\":\"x\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, strictWithoutKey.StatusCode);

        var strictContent = new StringContent("{\"sku\":\"x\"}");
        strictContent.Headers.Add("Idempotency-Key", "strict-order");
        var strictWithKey = await client.PostAsync("/strict-orders", strictContent);
        Assert.Equal(HttpStatusCode.Created, strictWithKey.StatusCode);
    }

    [Fact]
    public async Task MaxRequestBodyBytes_override_applies_only_to_that_endpoint()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        var body = "{\"sku\":\"" + new string('x', 50) + "\"}"; // well over the 10-byte endpoint override

        // /small-body-orders overrides MaxRequestBodyBytes to 10, so this body bypasses idempotency there.
        var first = new StringContent(body);
        first.Headers.Add("Idempotency-Key", "small-body-order");
        await client.PostAsync("/small-body-orders", first);

        var second = new StringContent(body);
        second.Headers.Add("Idempotency-Key", "small-body-order");
        await client.PostAsync("/small-body-orders", second);

        Assert.Equal(2, host.HandlerExecutionCount);

        // Same body on /orders stays under the app-wide 1 MiB default, so it still deduplicates.
        var thirdContent = new StringContent(body);
        thirdContent.Headers.Add("Idempotency-Key", "regular-order");
        await client.PostAsync("/orders", thirdContent);

        var fourthContent = new StringContent(body);
        fourthContent.Headers.Add("Idempotency-Key", "regular-order");
        await client.PostAsync("/orders", fourthContent);

        Assert.Equal(3, host.HandlerExecutionCount); // +1 for /orders, not +2
    }
}
