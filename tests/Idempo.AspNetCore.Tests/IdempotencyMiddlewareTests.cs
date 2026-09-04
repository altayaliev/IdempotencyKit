using System.Net;
using Idempo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Fact]
    public async Task Key_longer_than_MaxKeyLength_is_rejected_before_reaching_the_store()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyOptions>(o => o.MaxKeyLength = 20)))
            .CreateClient();

        var content = new StringContent("{\"sku\":\"x\"}");
        content.Headers.Add("Idempotency-Key", new string('k', 21));
        var response = await client.PostAsync("/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Key_at_or_under_MaxKeyLength_is_accepted()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyOptions>(o => o.MaxKeyLength = 20)))
            .CreateClient();

        var content = new StringContent("{\"sku\":\"x\"}");
        content.Headers.Add("Idempotency-Key", new string('k', 20));
        var response = await client.PostAsync("/orders", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Same_key_and_body_but_different_query_string_is_a_conflict()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();

        var first = new StringContent("{\"sku\":\"x\"}");
        first.Headers.Add("Idempotency-Key", "order-query");
        await client.PostAsync("/orders?discount=10", first);

        var second = new StringContent("{\"sku\":\"x\"}");
        second.Headers.Add("Idempotency-Key", "order-query");
        var response = await client.PostAsync("/orders?discount=20", second);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Request_body_larger_than_MaxRequestBodyBytes_bypasses_idempotency_entirely()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyOptions>(o => o.MaxRequestBodyBytes = 10)))
            .CreateClient();

        var body = "{\"sku\":\"" + new string('x', 50) + "\"}"; // well over 10 bytes
        var first = new StringContent(body);
        first.Headers.Add("Idempotency-Key", "order-big-body");
        var firstResponse = await client.PostAsync("/orders", first);

        var second = new StringContent(body);
        second.Headers.Add("Idempotency-Key", "order-big-body");
        var secondResponse = await client.PostAsync("/orders", second);

        // No idempotency protection applies once the body is over the limit — both requests
        // reach the handler, exactly like a normal unprotected endpoint would.
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(2, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task KeyResolver_can_scope_the_same_raw_key_by_tenant()
    {
        using var host = new TestHost();
        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<IdempotencyHttpOptions>(o =>
                    o.KeyResolver = (context, rawKey) => $"{context.Request.Headers["X-Tenant"]}:{rawKey}")))
            .CreateClient();

        var tenantAContent = new StringContent("{\"sku\":\"x\"}");
        tenantAContent.Headers.Add("Idempotency-Key", "shared-key");
        tenantAContent.Headers.Add("X-Tenant", "tenant-a");
        var tenantAResponse = await client.PostAsync("/orders", tenantAContent);

        var tenantBContent = new StringContent("{\"sku\":\"x\"}");
        tenantBContent.Headers.Add("Idempotency-Key", "shared-key");
        tenantBContent.Headers.Add("X-Tenant", "tenant-b");
        var tenantBResponse = await client.PostAsync("/orders", tenantBContent);

        // Same raw Idempotency-Key, but different tenants resolve to different store keys —
        // both execute, neither is treated as a duplicate of the other.
        Assert.Equal(HttpStatusCode.Created, tenantAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, tenantBResponse.StatusCode);
        Assert.Equal(2, host.HandlerExecutionCount);

        // Same tenant, same raw key, retried — still deduplicates as usual.
        var tenantARetryContent = new StringContent("{\"sku\":\"x\"}");
        tenantARetryContent.Headers.Add("Idempotency-Key", "shared-key");
        tenantARetryContent.Headers.Add("X-Tenant", "tenant-a");
        await client.PostAsync("/orders", tenantARetryContent);

        Assert.Equal(2, host.HandlerExecutionCount);
    }

    [Fact]
    public async Task Handler_success_is_still_returned_to_the_caller_even_if_persisting_the_result_fails()
    {
        using var host = new TestHost();
        CompleteFailingStore? failingStore = null;

        using var client = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore>(_ =>
                    failingStore = new CompleteFailingStore(new InMemoryIdempotencyStore())))))
            .CreateClient();

        // Prime the replacement store via one throwaway call, then arm it to fail the next Complete.
        var primeContent = new StringContent("{\"sku\":\"prime\"}");
        primeContent.Headers.Add("Idempotency-Key", "prime");
        await client.PostAsync("/orders", primeContent);
        failingStore!.FailNextComplete = true;

        var content = new StringContent("{\"sku\":\"x\"}");
        content.Headers.Add("Idempotency-Key", "order-complete-fails");
        var response = await client.PostAsync("/orders", content);

        // The handler ran and genuinely succeeded — the caller must see that real result, not a
        // 500, even though caching it for replay failed behind the scenes.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("orderId", body);
    }
}
