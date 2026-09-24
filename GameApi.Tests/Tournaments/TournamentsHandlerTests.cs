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
            LastGameAt = DateTimeOffset.UtcNow,
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        var tournament = Assert.Single(ok.Value!.Tournaments);
        Assert.Equal(500, tournament.TournamentParentId);
        Assert.Equal("Winter Cup", tournament.TournamentParentName);
    }

    [Fact]
    public async Task IndexAsync_OrdersByLastGameAtDescending_RegardlessOfWhenComputed()
    {
        using var db = NewInMemoryDb();
        var baseTime = DateTimeOffset.UtcNow;
        db.Tournaments.Add(new Tournament
        {
            TournamentParentId = 500,
            TotalGames = 1,
            TotalPlayers = 1,
            LastGameAt = baseTime.AddDays(-10),
            // Recomputed most recently, but its games are the oldest --
            // ComputedAt must not win the sort.
            ComputedAt = baseTime,
        });
        db.Tournaments.Add(new Tournament
        {
            TournamentParentId = 600,
            TotalGames = 1,
            TotalPlayers = 1,
            LastGameAt = baseTime,
            ComputedAt = baseTime.AddDays(-10),
        });
        await db.SaveChangesAsync();

        var result = await TournamentsHandler.IndexAsync(db, CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentsResponse>>(result);
        Assert.Equal(600, ok.Value!.Tournaments[0].TournamentParentId);
        Assert.Equal(500, ok.Value.Tournaments[1].TournamentParentId);
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

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
