using System.Net;
using Idempo;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Handler_exception_releases_the_key_so_a_retry_can_execute_again()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        var first = new StringContent("{}");
        first.Headers.Add("Idempotency-Key", "order-failing");
        var firstResponse = await client.PostAsync("/failing", first);

        var second = new StringContent("{}");
        second.Headers.Add("Idempotency-Key", "order-failing");
        var secondResponse = await client.PostAsync("/failing", second);

        // Both attempts fail (the handler always throws in this test), but the important
        // guarantee is that the reservation was released after the first failure: the second
        // request must reach the handler again rather than being stuck as "in progress" forever.
        Assert.Equal(HttpStatusCode.InternalServerError, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, secondResponse.StatusCode);
        Assert.Equal(2, host.FailingHandlerExecutionCount);
    }

    [Fact]
    public async Task Missing_header_is_rejected_when_required()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyOptions>(o => o.RequireHeaderOnMutatingRequests = true)))
            .CreateClient();

        var withoutKey = await client.PostAsync("/orders", new StringContent("{\"sku\":\"x\"}"));

        var withKeyContent = new StringContent("{\"sku\":\"x\"}");
        withKeyContent.Headers.Add("Idempotency-Key", "order-required");
        var withKey = await client.PostAsync("/orders", withKeyContent);

        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withKey.StatusCode);
    }

    [Fact]
    public async Task Custom_header_name_is_honored_instead_of_the_default()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyOptions>(o => o.HeaderName = "X-My-Idempotency-Key")))
            .CreateClient();

        // The default header name is now ignored, so two "duplicate" requests using it both execute.
        var defaultHeaderFirst = new StringContent("{\"sku\":\"default-header\"}");
        defaultHeaderFirst.Headers.Add("Idempotency-Key", "ignored-key");
        await client.PostAsync("/orders", defaultHeaderFirst);

        var defaultHeaderSecond = new StringContent("{\"sku\":\"default-header\"}");
        defaultHeaderSecond.Headers.Add("Idempotency-Key", "ignored-key");
        await client.PostAsync("/orders", defaultHeaderSecond);

        Assert.Equal(2, host.HandlerExecutionCount);

        // The configured custom header is honored and deduplicates as usual.
        var customHeaderFirst = new StringContent("{\"sku\":\"custom-header\"}");
        customHeaderFirst.Headers.Add("X-My-Idempotency-Key", "custom-key");
        var first = await client.PostAsync("/orders", customHeaderFirst);

        var customHeaderSecond = new StringContent("{\"sku\":\"custom-header\"}");
        customHeaderSecond.Headers.Add("X-My-Idempotency-Key", "custom-key");
        var second = await client.PostAsync("/orders", customHeaderSecond);

        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(3, host.HandlerExecutionCount);
    }
}
