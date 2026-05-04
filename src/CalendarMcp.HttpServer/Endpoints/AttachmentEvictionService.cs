using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Endpoints;

/// <summary>
/// Periodically removes expired entries from <see cref="IAttachmentStore"/>.
/// Without this the store would still reject expired entries on consume, but
/// memory would only be reclaimed when an upload triggered the lazy sweep.
/// </summary>
public sealed class AttachmentEvictionService(
    IAttachmentStore store,
    ILogger<AttachmentEvictionService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Attachment eviction sweeper started, interval {Interval}", SweepInterval);
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    store.EvictExpired();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Attachment eviction sweep failed; will retry");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
