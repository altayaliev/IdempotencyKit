using Idempo.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Idempo.AspNetCore.Tests;

/// <summary>A minimal in-process host exercising the idempotency middleware end to end.</summary>
public sealed class TestHost : WebApplicationFactory<TestHost>
{
    public int HandlerExecutionCount;
    public int FailingHandlerExecutionCount;

    // The test assembly has no Program/Main for WebApplicationFactory's default entry-point
    // discovery to find, so the host builder is provided explicitly here instead. The base
    // class wraps this with ConfigureWebHost (below) and UseTestServer() automatically.
    protected override IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder();

    // WebApplicationFactory's own content-root guess (based on this assembly's name) doesn't
    // account for the tests/ subfolder in this repo layout, so it is corrected here, right
    // before the host is built, overriding whatever ConfigureHostBuilder already set.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddIdempo();
        });

        builder.Configure(app =>
        {
            // TestServer, unlike Kestrel, rethrows an unhandled exception to the calling
            // HttpClient instead of turning it into a 500 response. Every real app has some
            // form of exception handling in its pipeline, so one is added here too — this lets
            // the test observe the same "500 now, but the key is usable again" behavior a real
            // deployment would see.
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            });

            app.UseRouting();
            app.UseIdempo();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/orders", async context =>
                {
                    Interlocked.Increment(ref HandlerExecutionCount);
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"orderId\":{HandlerExecutionCount}}}");
                });

                endpoints.MapPost("/pings", () => "pong").WithMetadata(new IdempotencyIgnoreAttribute());

                endpoints.MapPost("/failing", context =>
                {
                    Interlocked.Increment(ref FailingHandlerExecutionCount);
                    throw new InvalidOperationException("Simulated handler failure.");
                });
            });
        });
    }
}
