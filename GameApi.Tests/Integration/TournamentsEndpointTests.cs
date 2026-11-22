using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameApi.Data;
using GameApi.Data.Entities;
using GameApi.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameApi.Tests.Integration;

public sealed class TournamentsEndpointTests : IAsyncLifetime
{
    private readonly GameApiFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetTournaments_ReturnsUnauthorized_WhenNoAuthorizationHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTournaments_ReturnsUnauthorized_WhenTokenIsNotAValidJwt()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTournaments_ReturnsForbidden_WhenTokenIsValidButLacksTheGameHistoryScope()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTokenHelper.GenerateToken(scope: "read-decks"));

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTournaments_ReturnsOk_WhenTokenHasTheGameHistoryScope()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken());

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The adjustment endpoint is a privileged action (rewriting a recorded
    // result), so it's deliberately NOT gated on the "bga-game-history"
    // scope every reader holds -- it takes its own dedicated API key
    // (ApiKeys:Adjustment), same convention as bga-api's per-consumer keys.

    [Fact]
    public async Task PostAdjustment_ReturnsUnauthorized_WhenNoAuthorizationHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/tournaments/500/players/p1/adjustment",
            new { winsAdjustment = 1, lossesAdjustment = 0, note = "test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ReturnsUnauthorized_WhenAGameHistoryScopedTokenIsPresented()
    {
        // A valid, correctly-scoped read token must NOT unlock this endpoint
        // -- read access and "may rewrite results" are different rights.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken());

        var response = await client.PostAsJsonAsync(
            "/api/tournaments/500/players/p1/adjustment",
            new { winsAdjustment = 1, lossesAdjustment = 0, note = "test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ReturnsOk_WhenTheAdjustmentApiKeyIsPresented()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameApiDbContext>();
        db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 500, BgaUserId = "p1" });
        await db.SaveChangesAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.AdjustmentApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/tournaments/500/players/p1/adjustment",
            new { winsAdjustment = 1, lossesAdjustment = 0, note = "test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPlayers_ExposesReunionUserIdAndPseudo_AlongsideBgaName()
    {
        await SeedAsync(db =>
        {
            db.Tournaments.Add(new Tournament { TournamentParentId = 500, TournamentParentName = "Winter Cup", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament
            {
                TournamentParentId = 500, BgaUserId = "8412", BgaName = "BgaName", ReunionUserId = "kc-sub", ReunionPseudo = "Pseudo",
            });
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken());

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/tournaments/500/players"));

        var player = json.RootElement.GetProperty("players")[0];
        Assert.Equal("BgaName", player.GetProperty("bgaName").GetString());
        Assert.Equal("kc-sub", player.GetProperty("reunionUserId").GetString());
        Assert.Equal("Pseudo", player.GetProperty("pseudo").GetString());
    }

    // GET /api/tournament-decks is DeckApi's service-to-service feed: its own
    // ApiKeys:DeckApi secret, not the bga-game-history policy.

    [Fact]
    public async Task GetTournamentDecks_ReturnsUnauthorized_WhenNoAuthorizationHeader()
    {
        var response = await _factory.CreateClient().GetAsync("/api/tournament-decks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTournamentDecks_ReturnsUnauthorized_WhenTheKeyIsWrong()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-deckapi-key");

        var response = await client.GetAsync("/api/tournament-decks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTournamentDecks_ReturnsUnauthorized_ForAGameHistoryScopedTokenOrTheAdjustmentKey()
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournament-decks")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.AdjustmentApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournament-decks")).StatusCode);
    }

    [Fact]
    public async Task GetTournamentDecks_ReturnsUnauthorized_WhenNoKeyIsConfigured()
    {
        using var unconfigured = _factory.WithWebHostBuilder(b => b.UseSetting("ApiKeys:DeckApi", ""));
        var client = unconfigured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.DeckApiKey);

        var response = await client.GetAsync("/api/tournament-decks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTournamentDecks_ReturnsBadRequest_ForAMalformedCursor()
    {
        var response = await DeckApiClient().GetAsync("/api/tournament-decks?after=%%%not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTournamentDecks_ReturnsTheContractShape_WithoutBgaNameOrAdminAdjustments()
    {
        var lastPlayedAt = new DateTimeOffset(2026, 11, 12, 20, 1, 0, TimeSpan.Zero);
        await SeedAsync(db =>
        {
            db.Tournaments.Add(new Tournament { TournamentParentId = 123, TournamentParentName = "Winter Cup", Mode = "Frontier", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament
            {
                TournamentParentId = 123, BgaUserId = "8412", BgaName = "HiddenBgaName",
                ReunionUserId = "kc-sub", ReunionPseudo = "ReunionPseudo",
                AdminWinsAdjustment = 2, AdminAdjustmentNote = "n",
                DecksJson = JsonSerializer.Serialize(new[] { new DeckUsage("deckA", 4, 3, 1, lastPlayedAt) }),
                ComputedAt = T0,
            });
            // No ReUnion link yet, and DecksJson written before per-deck stats existed: still listed.
            db.PlayerTournaments.Add(new PlayerTournament
            {
                TournamentParentId = 123, BgaUserId = "9000", DecksJson = """[{"Deck":"deckB","Games":2}]""", ComputedAt = T0,
            });
        });

        using var json = JsonDocument.Parse(await DeckApiClient().GetStringAsync("/api/tournament-decks"));
        var root = json.RootElement;

        Assert.False(root.GetProperty("hasMore").GetBoolean());
        Assert.NotNull(root.GetProperty("next").GetString());
        var players = root.GetProperty("players");
        Assert.Equal(2, players.GetArrayLength());

        var linked = players[0];
        Assert.Equal(
            ["tournamentParentId", "tournamentName", "mode", "bgaUserId", "reunionUserId", "pseudo", "decks", "computedAt"],
            linked.EnumerateObject().Select(p => p.Name));
        Assert.Equal(123, linked.GetProperty("tournamentParentId").GetInt64());
        Assert.Equal("Winter Cup", linked.GetProperty("tournamentName").GetString());
        Assert.Equal("Frontier", linked.GetProperty("mode").GetString());
        Assert.Equal("8412", linked.GetProperty("bgaUserId").GetString());
        Assert.Equal("kc-sub", linked.GetProperty("reunionUserId").GetString());
        Assert.Equal("ReunionPseudo", linked.GetProperty("pseudo").GetString());
        Assert.Equal(T0, linked.GetProperty("computedAt").GetDateTimeOffset());
        var deck = linked.GetProperty("decks")[0];
        Assert.Equal(["deck", "games", "wins", "losses", "lastPlayedAt"], deck.EnumerateObject().Select(p => p.Name));
        Assert.Equal("deckA", deck.GetProperty("deck").GetString());
        Assert.Equal(4, deck.GetProperty("games").GetInt32());
        Assert.Equal(3, deck.GetProperty("wins").GetInt32());
        Assert.Equal(1, deck.GetProperty("losses").GetInt32());
        Assert.Equal(lastPlayedAt, deck.GetProperty("lastPlayedAt").GetDateTimeOffset());

        var unlinked = players[1];
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("reunionUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("pseudo").ValueKind);
        var legacyDeck = unlinked.GetProperty("decks")[0];
        Assert.Equal(0, legacyDeck.GetProperty("wins").GetInt32());
        Assert.Equal(JsonValueKind.Null, legacyDeck.GetProperty("lastPlayedAt").ValueKind);
    }

    [Fact]
    public async Task GetTournamentDecks_PagesThroughComputedAtTies_WithoutSkippingOrRepeating_AndTheCursorIsStable()
    {
        // 1 row before T0, 5 rows tied on T0 across two tournaments, 1 row after.
        await SeedAsync(db =>
        {
            db.Tournaments.Add(new Tournament { TournamentParentId = 1, ComputedAt = T0 });
            db.Tournaments.Add(new Tournament { TournamentParentId = 2, ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 2, BgaUserId = "z", ComputedAt = T0.AddSeconds(-1) });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 2, BgaUserId = "b", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 1, BgaUserId = "c", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 2, BgaUserId = "a", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 1, BgaUserId = "a", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 1, BgaUserId = "b", ComputedAt = T0 });
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 1, BgaUserId = "y", ComputedAt = T0.AddSeconds(1) });
        });
        var client = DeckApiClient();

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        while (true)
        {
            var (keys, next, hasMore) = await GetDeckPageAsync(client, cursor, pageSize: 2);
            pages++;
            seen.AddRange(keys);
            Assert.NotNull(next);
            cursor = next;
            if (!hasMore)
            {
                break;
            }
        }

        Assert.Equal(4, pages);
        Assert.Equal(["2/z", "1/a", "1/b", "1/c", "2/a", "2/b", "1/y"], seen);

        // Polling with the last `next` before anything changed: empty page, same cursor handed back.
        var (emptyKeys, sameCursor, emptyHasMore) = await GetDeckPageAsync(client, cursor, pageSize: 2);
        Assert.Empty(emptyKeys);
        Assert.Equal(cursor, sameCursor);
        Assert.False(emptyHasMore);

        // A row recomputed later (ComputedAt moves) and a brand-new row both show up on the next poll, and only they do.
        await SeedAsync(db =>
        {
            var changed = db.PlayerTournaments.Single(p => p.TournamentParentId == 1 && p.BgaUserId == "a");
            changed.ComputedAt = T0.AddMinutes(5);
            db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 2, BgaUserId = "new", ComputedAt = T0.AddMinutes(5) });
        });
        var (afterChange, _, afterChangeHasMore) = await GetDeckPageAsync(client, cursor, pageSize: 200);
        Assert.Equal(["1/a", "2/new"], afterChange);
        Assert.False(afterChangeHasMore);
    }

    [Fact]
    public async Task GetTournamentDecks_ClampsPageSize()
    {
        await SeedAsync(db =>
        {
            for (var i = 0; i < 3; i++)
            {
                db.PlayerTournaments.Add(new PlayerTournament { TournamentParentId = 1, BgaUserId = $"p{i}", ComputedAt = T0 });
            }
        });
        var client = DeckApiClient();

        var (defaulted, _, defaultedHasMore) = await GetDeckPageAsync(client, null, pageSize: 0);
        Assert.Equal(3, defaulted.Count);
        Assert.False(defaultedHasMore);

        var (capped, _, _) = await GetDeckPageAsync(client, null, pageSize: 100_000);
        Assert.Equal(3, capped.Count);
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 20, 5, 0, TimeSpan.Zero);

    private HttpClient DeckApiClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.DeckApiKey);
        return client;
    }

    private async Task SeedAsync(Action<GameApiDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameApiDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task<(List<string> Keys, string? Next, bool HasMore)> GetDeckPageAsync(HttpClient client, string? after, int pageSize)
    {
        var url = $"/api/tournament-decks?pageSize={pageSize}" + (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var keys = root.GetProperty("players").EnumerateArray()
            .Select(p => $"{p.GetProperty("tournamentParentId").GetInt64()}/{p.GetProperty("bgaUserId").GetString()}")
            .ToList();
        return (keys, root.GetProperty("next").GetString(), root.GetProperty("hasMore").GetBoolean());
    }
}
