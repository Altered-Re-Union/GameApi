using System.Text.Json;
using GameApi.Data;
using GameApi.Data.Entities;
using GameApi.Tournaments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tests.Tournaments;

public sealed class TournamentsHandlerTests
{
    [Fact]
    public async Task IndexAsync_ReturnsEveryTournament()
    {
        using var db = NewInMemoryDb();
        db.Tournaments.Add(new Tournament
        {
            TournamentParentId = 500,
            TournamentParentName = "Winter Cup",
            TotalGames = 3,
            TotalPlayers = 2,
            Mode = "Frontier",
            LastGameAt = DateTimeOffset.UtcNow,
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, new TournamentsQuery(null, null, null), CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        var tournament = Assert.Single(ok.Value!.Tournaments);
        Assert.Equal(500, tournament.TournamentParentId);
        Assert.Equal("Winter Cup", tournament.TournamentParentName);
        Assert.Equal("Frontier", tournament.Mode);
        Assert.Equal(1, ok.Value.TotalCount);
    }

    [Fact]
    public async Task IndexAsync_FiltersByModeAndMinPlayers()
    {
        using var db = NewInMemoryDb();
        db.Tournaments.Add(new Tournament { TournamentParentId = 1, Mode = "Frontier", TotalPlayers = 10, ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 2, Mode = "Sealed", TotalPlayers = 30, ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 3, Mode = "Frontier", TotalPlayers = 60, ComputedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, new TournamentsQuery("Frontier", 25, null), CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        var tournament = Assert.Single(ok.Value!.Tournaments);
        Assert.Equal(3, tournament.TournamentParentId);
    }

    [Fact]
    public async Task IndexAsync_FiltersByTournamentParentId()
    {
        using var db = NewInMemoryDb();
        db.Tournaments.Add(new Tournament { TournamentParentId = 1, ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 2, ComputedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, new TournamentsQuery(null, null, 2), CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        var tournament = Assert.Single(ok.Value!.Tournaments);
        Assert.Equal(2, tournament.TournamentParentId);
    }

    [Fact]
    public async Task IndexAsync_PaginatesAndReportsTotalCount()
    {
        using var db = NewInMemoryDb();
        for (var i = 1; i <= 5; i++)
        {
            db.Tournaments.Add(new Tournament
            {
                TournamentParentId = i,
                ComputedAt = DateTimeOffset.UtcNow,
                LastGameAt = DateTimeOffset.UtcNow.AddMinutes(i),
            });
        }
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, new TournamentsQuery(null, null, null, Page: 2, PageSize: 2), CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        Assert.Equal(2, ok.Value!.Tournaments.Count);
        Assert.Equal(5, ok.Value.TotalCount);
        Assert.Equal(2, ok.Value.Page);
        // Ordered by LastGameAt desc (5,4,3,2,1) -- page 2 of size 2 is [3,2].
        Assert.Equal(3, ok.Value.Tournaments[0].TournamentParentId);
        Assert.Equal(2, ok.Value.Tournaments[1].TournamentParentId);
    }

    [Fact]
    public async Task ModesAsync_ReturnsDistinctSortedModes()
    {
        using var db = NewInMemoryDb();
        db.Tournaments.Add(new Tournament { TournamentParentId = 1, Mode = "Sealed", ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 2, Mode = "Frontier", ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 3, Mode = "Frontier", ComputedAt = DateTimeOffset.UtcNow });
        db.Tournaments.Add(new Tournament { TournamentParentId = 4, Mode = null, ComputedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.ModesAsync(db, CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentModesResponse>>(result);
        Assert.Equal(["Frontier", "Sealed"], ok.Value!.Modes);
    }

    [Fact]
    public async Task PlayersAsync_ReturnsOnlyThatTournamentsPlayers_OrderedByEffectiveWins()
    {
        using var db = NewInMemoryDb();
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p1", Wins = 1 });
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p2", Wins = 3, AdminWinsAdjustment = 5 });
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 999, BgaUserId = "p3", Wins = 10 });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.PlayersAsync(db, 500, CancellationToken.None);

        var ok = Assert.IsType<Ok<PlayerTournamentsResponse>>(result);
        Assert.Equal(2, ok.Value!.Players.Count);
        // p2's effective total (3 + 5 admin) outranks p1's 1, even though the
        // computed Wins column alone wouldn't say so.
        Assert.Equal("p2", ok.Value.Players[0].BgaUserId);
        Assert.Equal("p1", ok.Value.Players[1].BgaUserId);
    }

    [Fact]
    public async Task PlayersAsync_ExposesEveryDeckThePlayerUsed_FromDecksJson()
    {
        using var db = NewInMemoryDb();
        var decksJson = JsonSerializer.Serialize(new[] { new DeckUsage("deckA", 2), new DeckUsage("deckB", 1) });
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p1", DecksJson = decksJson });
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p2", DecksJson = null });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.PlayersAsync(db, 500, CancellationToken.None);

        var ok = Assert.IsType<Ok<PlayerTournamentsResponse>>(result);
        var p1 = ok.Value!.Players.Single(p => p.BgaUserId == "p1");
        Assert.Equal([new DeckUsage("deckA", 2), new DeckUsage("deckB", 1)], p1.Decks);
        var p2 = ok.Value.Players.Single(p => p.BgaUserId == "p2");
        Assert.Empty(p2.Decks);
    }

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
