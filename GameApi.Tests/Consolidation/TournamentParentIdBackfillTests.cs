using GameApi.Consolidation;
using GameApi.Data;
using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tests.Consolidation;

public sealed class TournamentParentIdBackfillTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnceAsync_ReparentsTheStage1GroupsAndStage2_UnderStage2sIdMinusTwo()
    {
        using var db = NewInMemoryDb();
        AddGame(db, 1, 605542, "Monday Night Topcut - Season 1 - Week 3 - Stage 1 - Group 1", 605542);
        AddGame(db, 2, 605543, "Monday Night Topcut - Season 1 - Week 3 - Stage 1 - Group 2", 605543);
        AddGame(db, 3, 602988, "Monday Night Topcut - Season 1 - Week 3 - Stage 2", 602988);
        await db.SaveChangesAsync();

        var fixedCount = await TournamentParentIdBackfill.RunOnceAsync(db);

        Assert.Equal(3, fixedCount);
        Assert.Equal(602986, (await db.Games.SingleAsync(g => g.TableId == 1)).TournamentParentId);
        Assert.Equal(602986, (await db.Games.SingleAsync(g => g.TableId == 2)).TournamentParentId);
        Assert.Equal(602986, (await db.Games.SingleAsync(g => g.TableId == 3)).TournamentParentId);

        var tournament = await db.Tournaments.SingleAsync(t => t.TournamentParentId == 602986);
        Assert.Equal("Monday Night Topcut - Season 1 - Week 3", tournament.TournamentParentName);
        Assert.Equal(3, tournament.TotalGames);

        // The self-parented shells the three stages used to aggregate under are gone, not left as orphans.
        var oldShellIds = new long?[] { 605542, 605543, 602988 };
        Assert.False(await db.Tournaments.AnyAsync(t => oldShellIds.Contains(t.TournamentParentId)));
    }

    [Fact]
    public async Task RunOnceAsync_IsANoOp_OnceAlreadyApplied()
    {
        using var db = NewInMemoryDb();
        AddGame(db, 1, 602988, "Monday Night Topcut - Season 1 - Week 3 - Stage 2", 602988);
        await db.SaveChangesAsync();
        await TournamentParentIdBackfill.RunOnceAsync(db);

        var fixedCount = await TournamentParentIdBackfill.RunOnceAsync(db);

        Assert.Equal(0, fixedCount);
    }

    [Fact]
    public async Task RunOnceAsync_LeavesAGroupAlone_WhenTheStage2NameIsAmbiguous()
    {
        using var db = NewInMemoryDb();
        // Two unrelated events that happen to share the exact same "Stage 2" name.
        AddGame(db, 1, 700100, "Community Cup - Stage 2", 700100);
        AddGame(db, 2, 700200, "Community Cup - Stage 2", 700200);
        AddGame(db, 3, 700300, "Community Cup - Stage 1 - Group 1", 700300);
        await db.SaveChangesAsync();

        var fixedCount = await TournamentParentIdBackfill.RunOnceAsync(db);

        Assert.Equal(0, fixedCount);
        Assert.Equal(700100, (await db.Games.SingleAsync(g => g.TableId == 1)).TournamentParentId);
        Assert.Equal(700200, (await db.Games.SingleAsync(g => g.TableId == 2)).TournamentParentId);
        Assert.Equal(700300, (await db.Games.SingleAsync(g => g.TableId == 3)).TournamentParentId);
    }

    [Fact]
    public async Task RunOnceAsync_NeverTouchesAStage_BgaAlreadyNamedARealParentFor()
    {
        using var db = NewInMemoryDb();
        // Not self-parented -- a real parent was asserted (post-#630 sync), so this is a fact, not our default.
        AddGame(db, 1, 602988, "Monday Night Topcut - Season 1 - Week 3 - Stage 2", 999999);
        await db.SaveChangesAsync();

        var fixedCount = await TournamentParentIdBackfill.RunOnceAsync(db);

        Assert.Equal(0, fixedCount);
        Assert.Equal(999999, (await db.Games.SingleAsync(g => g.TableId == 1)).TournamentParentId);
    }

    private static void AddGame(GameApiDbContext db, long tableId, long tournamentId, string tournamentName, long tournamentParentId) =>
        db.Games.Add(new Game
        {
            TableId = tableId,
            ReceivedAt = BaseTime.AddMinutes(tableId),
            TournamentId = tournamentId,
            TournamentName = tournamentName,
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
