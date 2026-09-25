using BorisCodeStatus.Core;
using BorisCodeStatus.Core.State;
using Microsoft.Extensions.Hosting;

namespace BorisCodeStatus.Api;

/// <summary>
/// Periodically tops up the one figure hooks cannot supply — the Sonnet-only weekly window —
/// from the undocumented OAuth usage endpoint.
///
/// The cadence deliberately sits above <see cref="UsageApiClient.MinimumInterval"/>, and the
/// client applies its own cross-process throttle and 429 back-off on top. Everything about this
/// service is best-effort: a failed poll leaves the previous value in place and is not logged as
/// an error, because the endpoint being unavailable is an expected state, not a fault.
/// </summary>
internal sealed class UsageApiRefreshService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    // Let the tray finish starting and the first hooks land before touching the network.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly VitalsStateStore _store;

    public UsageApiRefreshService(VitalsStateStore store) => _store = store;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new UsageApiClient();

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(PollInterval);
            do
            {
                await RefreshAsync(client, stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RefreshAsync(UsageApiClient client, CancellationToken cancellationToken)
    {
        var sonnetWeek = await client.TryGetSonnetWeekAsync(cancellationToken).ConfigureAwait(false);
        if (sonnetWeek is null)
        {
            return;
        }

        _store.Update(current => current with
        {
            WeekSonnet = sonnetWeek,
            UsageApiLastSuccessUtc = DateTimeOffset.UtcNow,
        });
    }
}
