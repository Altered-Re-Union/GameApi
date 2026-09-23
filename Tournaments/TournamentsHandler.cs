using GameApi.Data;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tournaments;

public static class TournamentsHandler
{
    public static async Task<IResult> IndexAsync(GameApiDbContext db, CancellationToken cancellationToken)
    {
        var tournaments = await db.Tournaments
            .OrderByDescending(t => t.ComputedAt)
            .Select(t => new TournamentSummary(
                t.TournamentParentId, t.TournamentParentName, t.TotalGames, t.TotalPlayers, t.ComputedAt, t.RefreshedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new TournamentsResponse(tournaments));
    }

    public static async Task<IResult> PlayersAsync(GameApiDbContext db, long tournamentParentId, CancellationToken cancellationToken)
    {
        var players = await db.PlayerTournaments
            .Where(p => p.TournamentParentId == tournamentParentId)
            .OrderByDescending(p => p.Wins + p.AdminWinsAdjustment)
            .ThenBy(p => p.BgaUserId)
            .Select(p => new PlayerTournamentSummary(
                p.BgaUserId,
                p.BgaName,
                p.Wins,
                p.Losses,
                p.AdminWinsAdjustment,
                p.AdminLossesAdjustment,
                p.AdminAdjustmentNote,
                p.DecksPlayed,
                p.MainDeck,
                p.Faction,
                p.Hero,
                p.ComputedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PlayerTournamentsResponse(tournamentParentId, players));
    }
}

public sealed record TournamentSummary(
    long TournamentParentId,
    string? TournamentParentName,
    int TotalGames,
    int TotalPlayers,
    DateTimeOffset ComputedAt,
    DateTimeOffset RefreshedAt);

public sealed record TournamentsResponse(IReadOnlyList<TournamentSummary> Tournaments);

/// <summary>
/// Wins/Losses are the computed tally only -- a caller wanting the effective
/// total adds the Admin* adjustment itself (Wins + AdminWinsAdjustment), so
/// the computed and hand-corrected parts stay visible/auditable separately
/// rather than being pre-merged into one number.
/// </summary>
public sealed record PlayerTournamentSummary(
    string BgaUserId,
    string? BgaName,
    int Wins,
    int Losses,
    int AdminWinsAdjustment,
    int AdminLossesAdjustment,
    string? AdminAdjustmentNote,
    int DecksPlayed,
    string? MainDeck,
    string? Faction,
    string? Hero,
    DateTimeOffset ComputedAt);

public sealed record PlayerTournamentsResponse(long TournamentParentId, IReadOnlyList<PlayerTournamentSummary> Players);
