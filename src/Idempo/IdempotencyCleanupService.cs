using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Idempo;

/// <summary>
/// Periodically calls <see cref="IIdempotencyStore.PurgeExpiredAsync"/> so a key nobody ever
/// retries doesn't sit in the backend forever — <c>TryReserveAsync</c>'s own lazy reclaim only
/// ever touches a specific key when that exact key is looked up again. Registered automatically
/// by <c>AddIdempo()</c>.
/// </summary>
public sealed class IdempotencyCleanupService : BackgroundService
{
    private readonly IIdempotencyStore _store;
    private readonly IOptions<IdempotencyCleanupOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdempotencyCleanupService> _logger;

    public IdempotencyCleanupService(
        IIdempotencyStore store,
        IOptions<IdempotencyCleanupOptions> options,
        ILogger<IdempotencyCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.Value.Interval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var purged = await _store.PurgeExpiredAsync(stoppingToken);
                if (purged > 0)
                {
                    _logger.LogDebug("Idempo cleanup purged {Count} expired record(s).", purged);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient backend failure here (e.g. the database is briefly unreachable)
                // must not take the whole cleanup loop down — try again next interval.
                _logger.LogWarning(ex, "Idempo cleanup pass failed; will retry in {Interval}.", interval);
            }
        }
    }
}
