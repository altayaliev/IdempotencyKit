using System.Net;

namespace Idempo.AspNetCore.Tests;

public class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task Repeating_the_same_request_replays_the_first_response_without_rerunning_the_handler()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();
        var content = new StringContent("{\"sku\":\"abc\"}");
        content.Headers.Add("Idempotency-Key", "order-1");

        var first = await client.PostAsync("/orders", content);
        var firstBody = await first.Content.ReadAsStringAsync();

        var retryContent = new StringContent("{\"sku\":\"abc\"}");
        retryContent.Headers.Add("Idempotency-Key", "order-1");
        var second = await client.PostAsync("/orders", retryContent);
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(1, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Concurrent_identical_requests_only_execute_the_handler_once()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        Task<HttpResponseMessage> Send()
        {
            var content = new StringContent("{\"sku\":\"concurrent\"}");
            content.Headers.Add("Idempotency-Key", "order-concurrent");
            return client.PostAsync("/orders", content);
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Send()));

        // Whether any request actually lands in the InProgress (409) window is a timing detail —
        // with a fast in-memory handler, every request may instead see the already-Completed
        // result. The invariant that must always hold is that the handler runs exactly once and
        // every caller gets either the real result or a 409, never anything else.
        Assert.Equal(1, host.HandlerExecutionCount);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_body_returns_unprocessable_entity()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        var first = new StringContent("{\"sku\":\"a\"}");
        first.Headers.Add("Idempotency-Key", "order-conflict");
        await client.PostAsync("/orders", first);

        var second = new StringContent("{\"sku\":\"b\"}");
        second.Headers.Add("Idempotency-Key", "order-conflict");
        var response = await client.PostAsync("/orders", second);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Endpoint_marked_with_IdempotencyIgnore_bypasses_the_middleware_entirely()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        var first = await client.PostAsync("/pings", new StringContent(""));
        var second = await client.PostAsync("/pings", new StringContent(""));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Request_without_idempotency_key_passes_through_and_always_executes()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        await client.PostAsync("/orders", new StringContent("{\"sku\":\"no-key\"}"));
        await client.PostAsync("/orders", new StringContent("{\"sku\":\"no-key\"}"));

        Assert.Equal(2, host.HandlerExecutionCount);
    }
}
