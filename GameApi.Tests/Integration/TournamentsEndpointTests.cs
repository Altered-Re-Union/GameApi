using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameApi.Data;
using GameApi.Data.Entities;
using GameApi.Tests.TestSupport;
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
}
