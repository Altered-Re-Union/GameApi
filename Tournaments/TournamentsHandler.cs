using System.Text.Json;
using GameApi.Data;
using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tournaments;

public static class TournamentsHandler
{
    public static async Task<IResult> IndexAsync(GameApiDbContext db, [AsParameters] TournamentsQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var filtered = db.Tournaments.AsQueryable();
        if (query.TournamentParentId is { } tournamentParentId)
        {
            filtered = filtered.Where(t => t.TournamentParentId == tournamentParentId);
        }
        if (!string.IsNullOrWhiteSpace(query.Mode))
        {
            filtered = filtered.Where(t => t.Mode == query.Mode);
        }
        if (query.MinPlayers is { } minPlayers && minPlayers > 0)
        {
            filtered = filtered.Where(t => t.TotalPlayers >= minPlayers);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var tournaments = await filtered
            .OrderByDescending(t => t.LastGameAt ?? t.ComputedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TournamentSummary(
                t.TournamentParentId, t.TournamentParentName, t.TotalGames, t.TotalPlayers,
                t.Mode, t.LastGameAt, t.ComputedAt, t.RefreshedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new TournamentsResponse(tournaments, totalCount, page, pageSize));
    }

    public static async Task<IResult> ModesAsync(GameApiDbContext db, CancellationToken cancellationToken)
    {
        var modes = await db.Tournaments
            .Where(t => t.Mode != null)
            .Select(t => t.Mode!)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync(cancellationToken);

        return Results.Ok(new TournamentModesResponse(modes));
    }

    public static async Task<IResult> PlayersAsync(GameApiDbContext db, long tournamentParentId, CancellationToken cancellationToken)
    {
        var rows = await db.PlayerTournaments
            .Where(p => p.TournamentParentId == tournamentParentId)
            .OrderByDescending(p => p.Wins + p.AdminWinsAdjustment)
            .ThenBy(p => p.BgaUserId)
            .ToListAsync(cancellationToken);

        var players = rows.Select(p => new PlayerTournamentSummary(
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
            DeckUsagesOf(p.DecksJson),
            p.ComputedAt))
            .ToList();

        return Results.Ok(new PlayerTournamentsResponse(tournamentParentId, players));
    }

    /// <summary>Shared by both endpoints that return a PlayerTournamentSummary (see also AdjustmentHandler).</summary>
    internal static IReadOnlyList<DeckUsage> DeckUsagesOf(string? decksJson) =>
        string.IsNullOrEmpty(decksJson)
            ? []
            : JsonSerializer.Deserialize<List<DeckUsage>>(decksJson) ?? [];
}

public sealed record TournamentSummary(
    long TournamentParentId,
    string? TournamentParentName,
    int TotalGames,
    int TotalPlayers,
    string? Mode,
    DateTimeOffset? LastGameAt,
    DateTimeOffset ComputedAt,
    DateTimeOffset RefreshedAt);

public sealed record TournamentsResponse(IReadOnlyList<TournamentSummary> Tournaments, int TotalCount, int Page, int PageSize);

public sealed record TournamentModesResponse(IReadOnlyList<string> Modes);

/// <summary>
/// TournamentParentId is an exact-match filter, not a list-UI facet -- it's
/// what lets a caller that needs exactly one tournament (see the website's
/// trFetchLiveTournament()) fetch it directly instead of scanning pages of
/// the now-paginated index.
/// </summary>
public sealed record TournamentsQuery(string? Mode, int? MinPlayers, long? TournamentParentId, int Page = 1, int PageSize = 20);

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
    IReadOnlyList<DeckUsage> Decks,
    DateTimeOffset ComputedAt);

public sealed record PlayerTournamentsResponse(long TournamentParentId, IReadOnlyList<PlayerTournamentSummary> Players);
