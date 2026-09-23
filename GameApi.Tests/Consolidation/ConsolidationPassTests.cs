using AlteredBgaApi.Deckfmt;
using GameApi.Consolidation;
using GameApi.Data;
using GameApi.GameSync;
using GameApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tests.Consolidation;

public sealed class ConsolidationPassTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_TalliesWinsAndLosses_AcrossEveryGameOfTheTournament()
    {
        using var db = NewInMemoryDb();
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "Player One", isWinner: true),
                NewPlayer("p2", "Player Two", isWinner: false)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500,
                NewPlayer("p1", "Player One", isWinner: true),
                NewPlayer("p2", "Player Two", isWinner: false)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        var p2 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p2");
        Assert.Equal(2, p1.Wins);
        Assert.Equal(0, p1.Losses);
        Assert.Equal(0, p2.Wins);
        Assert.Equal(2, p2.Losses);
    }

    [Fact]
    public async Task RunAsync_GameWithNoRecognizedWinner_CountsTowardNeitherWinsNorLosses()
    {
        using var db = NewInMemoryDb();
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "Player One", isWinner: false),
                NewPlayer("p2", "Player Two", isWinner: false)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(0, p1.Wins);
        Assert.Equal(0, p1.Losses);
    }

    [Fact]
    public async Task RunAsync_MainDeck_PicksTheMostFrequentDeck()
    {
        using var db = NewInMemoryDb();
        var deckA = EncodeDeck("3 ALT_CORE_B_BR_19_C\n");
        var deckB = EncodeDeck("3 ALT_CORE_B_YZ_07_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, deckA)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500, NewPlayer("p1", "P1", false, deckA)),
            NewGame(3, BaseTime.AddMinutes(2), 500, "Winter Cup", 500, NewPlayer("p1", "P1", false, deckB)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(deckA, p1.MainDeck);
        Assert.Equal(2, p1.DecksPlayed);
    }

    [Fact]
    public async Task RunAsync_MainDeck_BreaksATieByFirstSeen()
    {
        using var db = NewInMemoryDb();
        var deckA = EncodeDeck("3 ALT_CORE_B_BR_19_C\n");
        var deckB = EncodeDeck("3 ALT_CORE_B_YZ_07_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, deckA)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500, NewPlayer("p1", "P1", false, deckB)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(deckA, p1.MainDeck);
    }

    [Fact]
    public async Task RunAsync_Faction_IsDerivedFromDecodingTheMainDeck()
    {
        using var db = NewInMemoryDb();
        // 3 Bravos cards to 1 Yzmir card -- majority (and only) faction is BR.
        var deck = EncodeDeck("3 ALT_CORE_B_BR_19_C\n1 ALT_CORE_B_BR_20_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, deck)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal("BR", p1.Faction);
    }

    [Fact]
    public async Task RunAsync_AdminAdjustment_SurvivesARecompute()
    {
        using var db = NewInMemoryDb();
        var firstPassClient = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true), NewPlayer("p2", "P2", false)));
        await ConsolidationPass.RunAsync(db, firstPassClient, pageSize: 200);

        var playerTournament = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        playerTournament.AdminWinsAdjustment = 5;
        playerTournament.AdminLossesAdjustment = 2;
        playerTournament.AdminAdjustmentNote = "Manually corrected for a BGA bug.";
        await db.SaveChangesAsync();

        var secondPassClient = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true), NewPlayer("p2", "P2", false)));
        await ConsolidationPass.RunAsync(db, secondPassClient, pageSize: 200);

        var updated = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(2, updated.Wins);
        Assert.Equal(5, updated.AdminWinsAdjustment);
        Assert.Equal(2, updated.AdminLossesAdjustment);
        Assert.Equal("Manually corrected for a BGA bug.", updated.AdminAdjustmentNote);
    }

    [Fact]
    public async Task RunAsync_TournamentParentName_IsDerivedFromTheStagesSharedPrefix()
    {
        using var db = NewInMemoryDb();
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 601, "Winter Cup - Round 1", 500,
                NewPlayer("p1", "P1", true), NewPlayer("p2", "P2", false)),
            NewGame(2, BaseTime.AddMinutes(1), 602, "Winter Cup - Final", 500,
                NewPlayer("p1", "P1", true), NewPlayer("p2", "P2", false)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var tournament = await db.Tournaments.SingleAsync(t => t.TournamentParentId == 500);
        Assert.Equal("Winter Cup", tournament.TournamentParentName);
        Assert.Equal(2, tournament.TotalGames);
        Assert.Equal(2, tournament.TotalPlayers);
    }

    [Fact]
    public async Task RunAsync_MirrorsGamesWithNoTournament_ButProducesNoAggregateForThem()
    {
        using var db = NewInMemoryDb();
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, tournamentId: null, tournamentName: null, tournamentParentId: null,
                NewPlayer("p1", "P1", true)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        Assert.Equal(1, await db.Games.CountAsync());
        Assert.Equal(0, await db.Tournaments.CountAsync());
        Assert.Equal(0, await db.PlayerTournaments.CountAsync());
    }

    [Fact]
    public async Task RunAsync_ATournamentThatLosesAllItsGamesToARealParent_HasItsStaleAggregateRemoved()
    {
        using var db = NewInMemoryDb();
        // First sight: BGA hadn't named a parent yet, table 1 self-parents to 100.
        var firstPassClient = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 100, "Winter Cup", 100, NewPlayer("p1", "P1", true)));
        await ConsolidationPass.RunAsync(db, firstPassClient, pageSize: 200);
        Assert.Equal(1, await db.Tournaments.CountAsync(t => t.TournamentParentId == 100));

        // Re-pulled (rematch/replay of the same table): BGA has since named
        // the real parent, 50.
        var secondPassClient = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 100, "Winter Cup", 50, NewPlayer("p1", "P1", true)));
        await ConsolidationPass.RunAsync(db, secondPassClient, pageSize: 200);

        Assert.False(await db.Tournaments.AnyAsync(t => t.TournamentParentId == 100));
        Assert.True(await db.Tournaments.AnyAsync(t => t.TournamentParentId == 50));
    }

    [Fact]
    public async Task RunAsync_AdvancesTheCursor_SoASecondPassOnlyAsksForWhatsNew()
    {
        using var db = NewInMemoryDb();
        var firstPassClient = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true)));
        await ConsolidationPass.RunAsync(db, firstPassClient, pageSize: 200);
        Assert.Null(firstPassClient.Requests[0].Since);

        var secondPassClient = new FakeGameSyncClient().EnqueueOnePage();
        await ConsolidationPass.RunAsync(db, secondPassClient, pageSize: 200);

        Assert.Equal(BaseTime, secondPassClient.Requests[0].Since);
    }

    private static GameSyncGame NewGame(
        long tableId, DateTimeOffset receivedAt, long? tournamentId, string? tournamentName, long? tournamentParentId,
        params GameSyncPlayer[] players) => new(
            tableId, receivedAt, "FRONTIER", tournamentId, tournamentName, tournamentParentId, null, players);

    private static GameSyncPlayer NewPlayer(string bgaUserId, string bgaName, bool isWinner, string? deck = null) =>
        new(bgaUserId, bgaName, isWinner, deck, null);

    private static string EncodeDeck(string text) => DeckfmtCodec.EncodeList(text);

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
