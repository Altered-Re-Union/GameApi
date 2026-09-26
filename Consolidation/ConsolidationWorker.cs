using GameApi.Data;
using GameApi.GameSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameApi.Consolidation;

/// <summary>
/// Thin IHostedService wrapper: runs ConsolidationPass on a timer. All the
/// actual logic (mirroring, aggregation) lives there so it can be unit
/// tested without a timer or a real HTTP call.
/// </summary>
public sealed class ConsolidationWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<ConsolidationOptions> options,
    ILogger<ConsolidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            return;
        }

        await RunTournamentParentIdBackfillSafeAsync(stoppingToken);
        await RunTournamentMetadataBackfillSafeAsync(stoppingToken);

        if (config.RunAtStartup)
        {
            await RunPassSafeAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(config.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPassSafeAsync(stoppingToken);
        }
    }

    /// <summary>Runs once per process lifetime in effect: TournamentParentIdBackfill is its own completion check, so every later call here is a cheap no-op query. Same fail-open handling as RunPassSafeAsync -- a bad restart must never block ordinary consolidation.</summary>
    private async Task RunTournamentParentIdBackfillSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameApiDbContext>();
            var fixedCount = await TournamentParentIdBackfill.RunOnceAsync(db, cancellationToken);
            if (fixedCount > 0)
            {
                logger.LogInformation("TournamentParentIdBackfill complete: tournamentsFixed={FixedCount}", fixedCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TournamentParentIdBackfill failed");
        }
    }

    /// <summary>Runs once per process lifetime in effect: TournamentMetadataBackfill is its own completion check, so every later call here is a cheap no-op query. Same fail-open handling as RunPassSafeAsync -- a bad restart must never block ordinary consolidation.</summary>
    private async Task RunTournamentMetadataBackfillSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameApiDbContext>();
            var backfilledCount = await TournamentMetadataBackfill.RunOnceAsync(db, cancellationToken);
            if (backfilledCount > 0)
            {
                logger.LogInformation("TournamentMetadataBackfill complete: tournamentsBackfilled={BackfilledCount}", backfilledCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TournamentMetadataBackfill failed");
        }
    }

    private async Task RunPassSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameApiDbContext>();
            var client = new GameSyncClient(httpClientFactory.CreateClient(GameSyncClientRegistration.HttpClientName));

            var result = await ConsolidationPass.RunAsync(db, client, options.Value.PageSize, cancellationToken);

            logger.LogInformation(
                "Consolidation pass complete: gamesPulled={GamesPulled} tournamentsTouched={TournamentsTouched}",
                result.GamesPulled, result.TournamentsTouched);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a bad upstream response or a transient DB hiccup
            // kill the timer loop -- the next tick just tries again.
            logger.LogError(ex, "Consolidation pass failed");
        }
    }
}
