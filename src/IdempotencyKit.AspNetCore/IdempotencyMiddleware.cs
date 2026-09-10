using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdempotencyKit.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdempotencyKit.AspNetCore;

/// <summary>
/// Deduplicates retried requests: a request carrying an <c>Idempotency-Key</c> header is executed
/// once, its result is cached, and any subsequent request with the same key and fingerprint replays
/// that result instead of re-running the handler. Must be registered after <c>UseRouting()</c> so
/// endpoint metadata (e.g. <see cref="IdempotencyIgnoreAttribute"/>) is available.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private static readonly HashSet<string> ExcludedReplayHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length", "Transfer-Encoding", "Content-Type"
    };

    private readonly RequestDelegate _next;

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IIdempotencyStore store,
        IIdempotencyFingerprintProvider fingerprintProvider,
        IOptions<IdempotencyOptions> optionsAccessor,
        IOptions<IdempotencyHttpOptions> httpOptionsAccessor,
        IdempotencyMetrics metrics,
        ILogger<IdempotencyMiddleware> logger)
    {
        var options = optionsAccessor.Value;
        var cancellationToken = context.RequestAborted;
        var endpoint = context.GetEndpoint();

        if (!options.Methods.Contains(context.Request.Method) ||
            endpoint?.Metadata.GetMetadata<IdempotencyIgnoreAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        // Per-endpoint overrides (see IdempotencyOptionsAttribute) layer on top of the app-wide
        // options; anything the endpoint didn't set falls back to the value above.
        var endpointOverride = endpoint?.Metadata.GetMetadata<IdempotencyOptionsAttribute>();
        var pendingTimeout = endpointOverride?.PendingTimeoutSeconds is { } pendingTimeoutSeconds
            ? TimeSpan.FromSeconds(pendingTimeoutSeconds)
            : options.PendingTimeout;
        var completedTtl = endpointOverride?.CompletedTtlSeconds is { } completedTtlSeconds
            ? TimeSpan.FromSeconds(completedTtlSeconds)
            : options.CompletedTtl;
        var requireHeaderOnMutatingRequests =
            endpointOverride?.RequireHeaderOnMutatingRequests ?? options.RequireHeaderOnMutatingRequests;
        var maxRequestBodyBytes = endpointOverride?.MaxRequestBodyBytes ?? options.MaxRequestBodyBytes;

        var rawKey = context.Request.Headers[options.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            if (requireHeaderOnMutatingRequests)
            {
                logger.LogWarning("Rejected {Method} {Path}: missing required '{Header}' header.",
                    context.Request.Method, context.Request.Path, options.HeaderName);
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                    "Idempotency-Key header is required",
                    $"This request must include a non-empty '{options.HeaderName}' header.");
                return;
            }

            await _next(context);
            return;
        }

        if (rawKey.Length > options.MaxKeyLength)
        {
            logger.LogWarning("Rejected {Method} {Path}: '{Header}' length {Length} exceeds MaxKeyLength {MaxKeyLength}.",
                context.Request.Method, context.Request.Path, options.HeaderName, rawKey.Length, options.MaxKeyLength);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "Idempotency-Key header is too long",
                $"The '{options.HeaderName}' header must not exceed {options.MaxKeyLength} characters.");
            return;
        }

        if (context.Request.ContentLength > maxRequestBodyBytes)
        {
            logger.LogWarning(
                "Skipping idempotency for {Method} {Path}: request body ({Length} bytes) exceeds MaxRequestBodyBytes ({Max}).",
                context.Request.Method, context.Request.Path, context.Request.ContentLength, maxRequestBodyBytes);
            await _next(context);
            return;
        }

        var key = httpOptionsAccessor.Value.KeyResolver(context, rawKey);

        using var activity = IdempotencyActivitySource.Instance.StartActivity("idempotencykit.reserve");
        activity?.SetTag("idempotencykit.key", key);

        var fingerprint = await ComputeFingerprintAsync(context, fingerprintProvider, cancellationToken);

        var reservation = await store.TryReserveAsync(
            key, fingerprint, pendingTimeout, completedTtl, cancellationToken);

        metrics.RecordReservation(reservation.Kind);
        activity?.SetTag("idempotencykit.outcome", reservation.Kind.ToString());
        logger.LogDebug("Idempotency check for key {Key} on {Method} {Path}: {Outcome}.",
            key, context.Request.Method, context.Request.Path, reservation.Kind);

        switch (reservation.Kind)
        {
            case IdempotencyReservationKind.Completed:
                await ReplayAsync(context, reservation.ExistingRecord!);
                return;

            case IdempotencyReservationKind.InProgress:
                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    "Request already in progress",
                    $"A request with idempotency key '{key}' is still being processed.");
                return;

            case IdempotencyReservationKind.Conflict:
                logger.LogWarning("Idempotency key '{Key}' was reused for a different request ({Method} {Path}).",
                    key, context.Request.Method, context.Request.Path);
                await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity,
                    "Idempotency key reused for a different request",
                    $"Idempotency key '{key}' was already used for a request with a different method, path, or body.");
                return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            await store.ReleaseAsync(key, cancellationToken);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var responseBody = buffer.ToArray();
        var record = new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = context.Response.StatusCode,
            ContentType = context.Response.ContentType,
            ResponseBody = responseBody,
            Headers = context.Response.Headers
                .Where(h => !ExcludedReplayHeaders.Contains(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.Select(v => v ?? string.Empty).ToArray()),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + completedTtl
        };

        try
        {
            await store.CompleteAsync(key, record, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The handler's own work already succeeded — the caller must still get that real
            // response below. Only *caching* the result for replay failed. The reservation is
            // left Pending: a retry within PendingTimeout correctly sees "in progress" (409); a
            // retry after PendingTimeout will re-run the handler, since this result was never
            // durably recorded. Logged at Error (not just a metric) because it is exactly the
            // failure mode idempotency exists to prevent, happening despite the safeguard.
            metrics.RecordCompleteFailure();
            logger.LogError(ex,
                "Idempotency key '{Key}' ({Method} {Path}): the handler succeeded but persisting its result " +
                "failed. Returning the real response now, but a retry after PendingTimeout may re-run the handler.",
                key, context.Request.Method, context.Request.Path);
        }

        await originalBody.WriteAsync(responseBody, cancellationToken);
    }

    private static async Task<string> ComputeFingerprintAsync(
        HttpContext context, IIdempotencyFingerprintProvider fingerprintProvider, CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();

        using var bodyBuffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(bodyBuffer, cancellationToken);
        context.Request.Body.Position = 0;

        // Path + query string: an endpoint whose behavior depends on a query parameter (e.g.
        // ?discount=10 vs ?discount=20) must not be treated as the same request just because the
        // path and body happen to match.
        var pathAndQuery = (context.Request.Path.Value ?? string.Empty) + context.Request.QueryString.Value;

        return fingerprintProvider.Compute(context.Request.Method, pathAndQuery, bodyBuffer.ToArray());
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        context.Response.StatusCode = record.StatusCode ?? StatusCodes.Status200OK;

        if (record.Headers is not null)
        {
            foreach (var (headerName, values) in record.Headers)
            {
                context.Response.Headers[headerName] = values;
            }
        }

        if (record.ContentType is not null)
        {
            context.Response.ContentType = record.ContentType;
        }

        if (record.ResponseBody is { Length: > 0 })
        {
            await context.Response.Body.WriteAsync(record.ResponseBody, context.RequestAborted);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var payload = new ProblemDetailsPayload($"https://httpstatuses.io/{statusCode}", title, statusCode, detail);
        await JsonSerializer.SerializeAsync(
            context.Response.Body, payload, MiddlewareJsonContext.Default.ProblemDetailsPayload, context.RequestAborted);
    }
}

internal sealed record ProblemDetailsPayload(string Type, string Title, int Status, string Detail);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProblemDetailsPayload))]
internal sealed partial class MiddlewareJsonContext : JsonSerializerContext
{
}
