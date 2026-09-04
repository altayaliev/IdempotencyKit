using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Idempo.AspNetCore;

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
        IOptions<IdempotencyOptions> optionsAccessor)
    {
        var options = optionsAccessor.Value;
        var cancellationToken = context.RequestAborted;

        if (!options.Methods.Contains(context.Request.Method) ||
            context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyIgnoreAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var key = context.Request.Headers[options.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (options.RequireHeaderOnMutatingRequests)
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                    "Idempotency-Key header is required",
                    $"This request must include a non-empty '{options.HeaderName}' header.");
                return;
            }

            await _next(context);
            return;
        }

        var fingerprint = await ComputeFingerprintAsync(context, fingerprintProvider, cancellationToken);

        var reservation = await store.TryReserveAsync(
            key, fingerprint, options.PendingTimeout, options.CompletedTtl, cancellationToken);

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
            ExpiresAt = DateTimeOffset.UtcNow + options.CompletedTtl
        };
        await store.CompleteAsync(key, record, cancellationToken);

        await originalBody.WriteAsync(responseBody, cancellationToken);
    }

    private static async Task<string> ComputeFingerprintAsync(
        HttpContext context, IIdempotencyFingerprintProvider fingerprintProvider, CancellationToken cancellationToken)
    {
        context.Request.EnableBuffering();

        using var bodyBuffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(bodyBuffer, cancellationToken);
        context.Request.Body.Position = 0;

        return fingerprintProvider.Compute(context.Request.Method, context.Request.Path.Value ?? string.Empty, bodyBuffer.ToArray());
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
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            detail
        }, cancellationToken: context.RequestAborted);
    }
}
