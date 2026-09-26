using GameApi.Data;
using GameApi.Data.Entities;
using GameApi.Tournaments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tests.Tournaments;

public sealed class AdjustmentHandlerTests
{
    [Fact]
    public async Task HandleAsync_SetsTheAdjustmentAndNote_WhenThePlayerTournamentRowExists()
    {
        using var db = NewInMemoryDb();
        var decksJson = System.Text.Json.JsonSerializer.Serialize(new[] { new DeckUsage("deckA", 2) });
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p1", Wins = 3, Losses = 1, DecksJson = decksJson });
        await db.SaveChangesAsync();

        var result = await AdjustmentHandler.HandleAsync(
            db, 500, "p1", new AdjustmentRequest(1, 0, "Admin-awarded win for a BGA connection bug."), CancellationToken.None);

        var ok = Assert.IsType<Ok<PlayerTournamentSummary>>(result);
        var stored = await db.PlayerTournaments.SingleAsync();
        Assert.Equal(1, stored.AdminWinsAdjustment);
        Assert.Equal(0, stored.AdminLossesAdjustment);
        Assert.Equal("Admin-awarded win for a BGA connection bug.", stored.AdminAdjustmentNote);
        // The computed tally itself is untouched by the adjustment endpoint.
        Assert.Equal(3, stored.Wins);
        // The response reflects the player's full deck history, not just MainDeck.
        Assert.Equal([new DeckUsage("deckA", 2)], ok.Value!.Decks);
    }

    [Fact]
    public async Task HandleAsync_ReturnsBadRequest_WhenNoteIsMissing()
    {
        using var db = NewInMemoryDb();
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p1" });
        await db.SaveChangesAsync();

        var result = await AdjustmentHandler.HandleAsync(
            db, 500, "p1", new AdjustmentRequest(1, 0, ""), CancellationToken.None);

        var badRequest = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFound_WhenNoPlayerTournamentRowExists()
    {
        using var db = NewInMemoryDb();

        var result = await AdjustmentHandler.HandleAsync(
            db, 500, "p1", new AdjustmentRequest(1, 0, "note"), CancellationToken.None);

        var notFound = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
