using GameApi.Data;
using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tournaments;

/// <summary>
/// Sets a player's admin win/loss adjustment for one tournament -- manual
/// corrections for admin overrides, BGA bugs, or games played off-platform.
/// Additive on top of the computed Wins/Losses, and never touched by a
/// consolidation recompute (see Consolidation/ConsolidationPass).
///
/// A non-empty note is required: it's the whole point of the field -- an
/// adjustment with no recorded reason is not auditable.
/// </summary>
public static class AdjustmentHandler
{
    public static async Task<IResult> HandleAsync(
        GameApiDbContext db, long tournamentParentId, string bgaUserId, AdjustmentRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.BadRequest(new { error = "A request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Results.BadRequest(new { error = "note is required and must not be empty." });
        }

        var playerTournament = await db.PlayerTournaments
            .FirstOrDefaultAsync(p => p.TournamentParentId == tournamentParentId && p.BgaUserId == bgaUserId, cancellationToken);

        if (playerTournament is null)
        {
            return Results.NotFound(new { error = "No PlayerTournament row for that tournament/player." });
        }

        playerTournament.AdminWinsAdjustment = request.WinsAdjustment;
        playerTournament.AdminLossesAdjustment = request.LossesAdjustment;
        playerTournament.AdminAdjustmentNote = request.Note;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new PlayerTournamentSummary(
            playerTournament.BgaUserId,
            playerTournament.BgaName,
            playerTournament.Wins,
            playerTournament.Losses,
            playerTournament.AdminWinsAdjustment,
            playerTournament.AdminLossesAdjustment,
            playerTournament.AdminAdjustmentNote,
            playerTournament.DecksPlayed,
            playerTournament.MainDeck,
            playerTournament.Faction,
            playerTournament.Hero,
            playerTournament.ComputedAt));
    }
}

public sealed record AdjustmentRequest(int WinsAdjustment, int LossesAdjustment, string? Note);
