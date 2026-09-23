using GameApi.Consolidation;
using GameApi.Data;
using GameApi.GameSync;
using GameApi.Security;
using GameApi.Tournaments;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GameApiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("gameapidb")
        ?? throw new InvalidOperationException("ConnectionStrings:gameapidb is not configured.")));

// The consolidation worker's only outbound dependency: altered-bga-api's raw
// game feed. BaseUrl/ApiKey are read once at startup -- unlike some routes in
// that gateway, there's no reason to defer this, the worker needs both to do
// anything at all.
var bgaApiBaseUrl = builder.Configuration["AlteredBgaApi:BaseUrl"]
    ?? throw new InvalidOperationException("AlteredBgaApi:BaseUrl is not configured.");
var bgaApiKey = builder.Configuration["AlteredBgaApi:ApiKey"]
    ?? throw new InvalidOperationException("AlteredBgaApi:ApiKey is not configured.");
builder.Services.AddHttpClient(GameSyncClientRegistration.HttpClientName, client =>
    GameSyncClientRegistration.Configure(client, bgaApiBaseUrl, bgaApiKey));

builder.Services.Configure<ConsolidationOptions>(
    builder.Configuration.GetSection(ConsolidationOptions.SectionName));
builder.Services.AddHostedService<ConsolidationWorker>();

var app = builder.Build();

// Bearer key protecting every /api/* route below -- one shared secret for
// this whole service (read access to decklists/admin notes and the write
// access to admin adjustments both need it).
var gameApiKey = app.Configuration["ApiKeys:GameApi"];

bool IsAuthorized(HttpContext httpContext)
{
    const string bearerPrefix = "Bearer ";
    var header = httpContext.Request.Headers.Authorization.ToString();
    return header.StartsWith(bearerPrefix, StringComparison.Ordinal)
        && ApiKeyAuth.Matches(header[bearerPrefix.Length..], gameApiKey);
}

app.MapGet("/healthz", () => Results.Ok());

app.MapGet("/api/tournaments", async (HttpContext httpContext, GameApiDbContext db, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(httpContext))
    {
        return Results.Unauthorized();
    }

    return await TournamentsHandler.IndexAsync(db, cancellationToken);
});

app.MapGet("/api/tournaments/{tournamentParentId:long}/players", async (
    HttpContext httpContext, GameApiDbContext db, long tournamentParentId, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(httpContext))
    {
        return Results.Unauthorized();
    }

    return await TournamentsHandler.PlayersAsync(db, tournamentParentId, cancellationToken);
});

app.MapPost("/api/tournaments/{tournamentParentId:long}/players/{bgaUserId}/adjustment", async (
    HttpContext httpContext, GameApiDbContext db, long tournamentParentId, string bgaUserId,
    AdjustmentRequest? request, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(httpContext))
    {
        return Results.Unauthorized();
    }

    return await AdjustmentHandler.HandleAsync(db, tournamentParentId, bgaUserId, request, cancellationToken);
});

app.Run();

// Makes the top-level statements' implicit Program class public so
// WebApplicationFactory<Program> can host it from GameApi.Tests.
public partial class Program;
