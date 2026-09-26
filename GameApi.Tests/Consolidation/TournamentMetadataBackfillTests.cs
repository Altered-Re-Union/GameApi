using GameApi.Consolidation;
using GameApi.Data;
using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tests.Consolidation;

public sealed class TournamentMetadataBackfillTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnceAsync_FillsModeAndLastGameAt_ForATournamentMissingThem()
    {
        using var db = NewInMemoryDb();
        AddGame(db, 1, 500, "Frontier");
        AddGame(db, 2, 500, "Frontier");
        db.Tournaments.Add(new Tournament { TournamentParentId = 500, TotalGames = 2, ComputedAt = BaseTime });
        await db.SaveChangesAsync();

        var backfilledCount = await TournamentMetadataBackfill.RunOnceAsync(db);

        Assert.Equal(1, backfilledCount);
        var tournament = await db.Tournaments.SingleAsync(t => t.TournamentParentId == 500);
        Assert.Equal("Frontier", tournament.Mode);
        Assert.Equal(BaseTime.AddMinutes(2), tournament.LastGameAt);
    }

    [Fact]
    public async Task RunOnceAsync_IsANoOp_OnceAlreadyApplied()
    {
        using var db = NewInMemoryDb();
        AddGame(db, 1, 500, "Sealed");
        db.Tournaments.Add(new Tournament { TournamentParentId = 500, TotalGames = 1, ComputedAt = BaseTime });
        await db.SaveChangesAsync();
        await TournamentMetadataBackfill.RunOnceAsync(db);

        var backfilledCount = await TournamentMetadataBackfill.RunOnceAsync(db);

        Assert.Equal(0, backfilledCount);
    }

    private static void AddGame(GameApiDbContext db, long tableId, long tournamentParentId, string format) =>
        db.Games.Add(new Game
        {
            TableId = tableId,
            ReceivedAt = BaseTime.AddMinutes(tableId),
            Format = format,
            TournamentParentId = tournamentParentId,
        });

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
