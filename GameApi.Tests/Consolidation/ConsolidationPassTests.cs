using System.Text.Json;
using AlteredBgaApi.Deckfmt;
using GameApi.Consolidation;
using GameApi.Data;
using GameApi.Data.Entities;
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

        var decks = JsonSerializer.Deserialize<List<DeckUsage>>(p1.DecksJson!)!;
        Assert.Equal(
            [
                new DeckUsage(deckA, 2, Wins: 1, Losses: 0, LastPlayedAt: BaseTime.AddMinutes(1)),
                new DeckUsage(deckB, 1, Wins: 0, Losses: 0, LastPlayedAt: BaseTime.AddMinutes(2)),
            ],
            decks);
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
    public async Task RunAsync_Hero_IsNormalizedToTheCoreTierPrintRegardlessOfWhichSetTheDeckCardIsFrom()
    {
        using var db = NewInMemoryDb();
        // COREKS' AX_01 is the same hero character as CORE's AX_01.
        var deck = EncodeDeck("1 ALT_COREKS_B_AX_01_C\n3 ALT_CORE_B_BR_19_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, deck)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal("ALT_CORE_B_AX_01_C", p1.Hero);
    }

    [Fact]
    public async Task RunAsync_Hero_IsNormalizedForTheDusterEoleAndFugueTiers()
    {
        using var db = NewInMemoryDb();
        var dusterDeck = EncodeDeck("1 ALT_DUSTER_A_AX_85_C\n3 ALT_CORE_B_BR_19_C\n");
        var eoleDeck = EncodeDeck("1 ALT_EOLE_P_BR_105_C\n3 ALT_CORE_B_BR_19_C\n");
        var fugueDeck = EncodeDeck("1 ALT_FUGUE_B_YZ_130_C\n3 ALT_CORE_B_BR_19_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, dusterDeck)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500, NewPlayer("p2", "P2", true, eoleDeck)),
            NewGame(3, BaseTime.AddMinutes(2), 500, "Winter Cup", 500, NewPlayer("p3", "P3", true, fugueDeck)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        Assert.Equal("ALT_DUSTER_B_AX_85_C", (await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1")).Hero);
        Assert.Equal("ALT_EOLE_B_BR_105_C", (await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p2")).Hero);
        Assert.Equal("ALT_FUGUE_B_YZ_130_C", (await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p3")).Hero);
    }

    [Fact]
    public async Task RunAsync_Hero_IsNullWhenTheDeckHasNoRecognizedHeroCard()
    {
        using var db = NewInMemoryDb();
        var deck = EncodeDeck("3 ALT_CORE_B_BR_19_C\n1 ALT_CORE_B_BR_20_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500, NewPlayer("p1", "P1", true, deck)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Null(p1.Hero);
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

    [Fact]
    public async Task RunAsync_DeckUsages_CarryPerDeckWinsLossesAndLastPlayedAt()
    {
        using var db = NewInMemoryDb();
        var deckA = EncodeDeck("3 ALT_CORE_B_BR_19_C\n");
        var deckB = EncodeDeck("3 ALT_CORE_B_YZ_07_C\n");
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, deckA), NewPlayer("p2", "P2", false)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", false, deckA), NewPlayer("p2", "P2", true)),
            // No recognized winner: counts toward neither, but is still a game played with deckA.
            NewGame(3, BaseTime.AddMinutes(2), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", false, deckA), NewPlayer("p2", "P2", false)),
            NewGame(4, BaseTime.AddMinutes(3), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, deckB), NewPlayer("p2", "P2", false)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(2, p1.Wins);
        Assert.Equal(1, p1.Losses);
        var decks = JsonSerializer.Deserialize<List<DeckUsage>>(p1.DecksJson!)!;
        Assert.Equal(
            [
                new DeckUsage(deckA, 3, Wins: 1, Losses: 1, LastPlayedAt: BaseTime.AddMinutes(2)),
                new DeckUsage(deckB, 1, Wins: 1, Losses: 0, LastPlayedAt: BaseTime.AddMinutes(3)),
            ],
            decks);
    }

    [Fact]
    public async Task RunAsync_ReunionIdentity_IsMirroredAndTakenFromTheMostRecentGameThatCarriesIt()
    {
        using var db = NewInMemoryDb();
        var client = new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, reunionUserId: "kc-old", reunionPseudo: "OldPseudo"),
                NewPlayer("p2", "P2", false)),
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, reunionUserId: "kc-new", reunionPseudo: "NewPseudo"),
                NewPlayer("p2", "P2", false)),
            // A later game without a ReUnion link never erases the known one.
            NewGame(3, BaseTime.AddMinutes(2), 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true),
                NewPlayer("p2", "P2", false)));

        await ConsolidationPass.RunAsync(db, client, pageSize: 200);

        var mirrored = await db.PlayerGames.SingleAsync(p => p.TableId == 2 && p.BgaUserId == "p1");
        Assert.Equal("kc-new", mirrored.ReunionUserId);
        Assert.Equal("NewPseudo", mirrored.ReunionPseudo);

        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal("kc-new", p1.ReunionUserId);
        Assert.Equal("NewPseudo", p1.ReunionPseudo);

        var p2 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p2");
        Assert.Null(p2.ReunionUserId);
        Assert.Null(p2.ReunionPseudo);
    }

    [Fact]
    public async Task RunAsync_ComputedAt_MovesOnlyWhenTheRowsContentChanges_PseudoIncluded()
    {
        using var db = NewInMemoryDb();
        await ConsolidationPass.RunAsync(db, new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, reunionUserId: "kc-1", reunionPseudo: "Before"),
                NewPlayer("p2", "P2", false))), pageSize: 200);
        var first = await db.PlayerTournaments.AsNoTracking().SingleAsync(p => p.BgaUserId == "p1");

        // Same tournament recomputed (a game between other players), p1's content unchanged.
        await Task.Delay(20);
        await ConsolidationPass.RunAsync(db, new FakeGameSyncClient().EnqueueOnePage(
            NewGame(2, BaseTime.AddMinutes(1), 500, "Winter Cup", 500,
                NewPlayer("p3", "P3", true), NewPlayer("p4", "P4", false))), pageSize: 200);
        var unchanged = await db.PlayerTournaments.AsNoTracking().SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(first.ComputedAt, unchanged.ComputedAt);
        Assert.True(unchanged.RefreshedAt > first.RefreshedAt);

        // Table 1 re-pulled with only the pseudo changed.
        await Task.Delay(20);
        await ConsolidationPass.RunAsync(db, new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, reunionUserId: "kc-1", reunionPseudo: "After"),
                NewPlayer("p2", "P2", false))), pageSize: 200);
        var renamed = await db.PlayerTournaments.AsNoTracking().SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal("After", renamed.ReunionPseudo);
        Assert.True(renamed.ComputedAt > first.ComputedAt);
        // p2's row was recomputed in that same pass but didn't change.
        var p2 = await db.PlayerTournaments.AsNoTracking().SingleAsync(p => p.BgaUserId == "p2");
        Assert.Equal(first.ComputedAt, p2.ComputedAt);
    }

    [Fact]
    public void GameSyncPlayer_FeedRowWithoutReunionFields_StillDeserializes()
    {
        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var legacy = JsonSerializer.Deserialize<GameSyncPlayer>(
            """{"bgaUserId":"8412","bgaName":"P1","isWinner":true,"deck":"d","playedCards":null}""", webOptions)!;
        Assert.Equal("8412", legacy.BgaUserId);
        Assert.Null(legacy.ReunionUserId);
        Assert.Null(legacy.ReunionPseudo);

        var current = JsonSerializer.Deserialize<GameSyncPlayer>(
            """{"bgaUserId":"8412","isWinner":false,"reunionUserId":"kc-sub","reunionPseudo":"Pseudo"}""", webOptions)!;
        Assert.Equal("kc-sub", current.ReunionUserId);
        Assert.Equal("Pseudo", current.ReunionPseudo);
    }

    [Fact]
    public void DeckUsage_DecksJsonWrittenBeforePerDeckStats_StillReads()
    {
        var decks = JsonSerializer.Deserialize<List<DeckUsage>>("""[{"Deck":"deckA","Games":2}]""")!;

        Assert.Equal([new DeckUsage("deckA", 2, 0, 0, null)], decks);
    }

    [Fact]
    public async Task DeckStatsBackfill_RebuildsTournamentsWhoseDecksJsonPredatesPerDeckStats_ThenIsANoOp()
    {
        using var db = NewInMemoryDb();
        var deckA = EncodeDeck("3 ALT_CORE_B_BR_19_C\n");
        await ConsolidationPass.RunAsync(db, new FakeGameSyncClient().EnqueueOnePage(
            NewGame(1, BaseTime, 500, "Winter Cup", 500,
                NewPlayer("p1", "P1", true, deckA), NewPlayer("p2", "P2", false))), pageSize: 200);
        var p1 = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        p1.DecksJson = JsonSerializer.Serialize(new[] { new { Deck = deckA, Games = 1 } });
        await db.SaveChangesAsync();

        Assert.Equal(1, await DeckStatsBackfill.RunOnceAsync(db));

        var rebuilt = await db.PlayerTournaments.SingleAsync(p => p.BgaUserId == "p1");
        Assert.Equal(
            [new DeckUsage(deckA, 1, Wins: 1, Losses: 0, LastPlayedAt: BaseTime)],
            JsonSerializer.Deserialize<List<DeckUsage>>(rebuilt.DecksJson!)!);
        Assert.Equal(0, await DeckStatsBackfill.RunOnceAsync(db));
    }

    private static GameSyncGame NewGame(
        long tableId, DateTimeOffset receivedAt, long? tournamentId, string? tournamentName, long? tournamentParentId,
        params GameSyncPlayer[] players) => new(
            tableId, receivedAt, "FRONTIER", tournamentId, tournamentName, tournamentParentId, null, players);

    private static GameSyncPlayer NewPlayer(
        string bgaUserId, string bgaName, bool isWinner, string? deck = null,
        string? reunionUserId = null, string? reunionPseudo = null) =>
        new(bgaUserId, bgaName, isWinner, deck, null, reunionUserId, reunionPseudo);

    private static string EncodeDeck(string text) => DeckfmtCodec.EncodeList(text);

    private static GameApiDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GameApiDbContext(options);
    }
}
