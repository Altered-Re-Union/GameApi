using GameApi.Consolidation;
using GameApi.Data;
using GameApi.GameSync;
using GameApi.Security;
using GameApi.Tournaments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GameApiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("gameapidb")
        ?? throw new InvalidOperationException("ConnectionStrings:gameapidb is not configured.")));

// The consolidation worker's only outbound dependency: altered-bga-api's raw
// game feed. BaseUrl/ApiKey are read once at startup -- unlike some routes in
// that gateway, there's no reason to defer this, the worker needs both to do
// anything at all. This is a plain API key (AlteredBgaApi:ApiKey, matching
// bga-api's own ApiKeys:GameSync) -- bga-api authenticates every internal
// consumer with its own static secret, not AlteredAuth.
var bgaApiBaseUrl = builder.Configuration["AlteredBgaApi:BaseUrl"]
    ?? throw new InvalidOperationException("AlteredBgaApi:BaseUrl is not configured.");
var bgaApiKey = builder.Configuration["AlteredBgaApi:ApiKey"]
    ?? throw new InvalidOperationException("AlteredBgaApi:ApiKey is not configured.");
builder.Services.AddHttpClient(GameSyncClientRegistration.HttpClientName, client =>
    GameSyncClientRegistration.Configure(client, bgaApiBaseUrl, bgaApiKey));

builder.Services.Configure<ConsolidationOptions>(
    builder.Configuration.GetSection(ConsolidationOptions.SectionName));
builder.Services.AddHostedService<ConsolidationWorker>();

// GameApi's own /api/* routes (below) are gated on an AlteredAuth (Keycloak)
// bearer token carrying the "bga-game-history" scope, not an API key -- this
// is the surface a website/other reader calls, so it goes through the same
// user-facing identity provider as the rest of the ecosystem rather than a
// shared secret. Keycloak:Authority drives standard OIDC discovery/JWKS
// validation (RS256) for real deployments.
//
// Keycloak:TestSigningKey is test-only (never set in Development/Production
// config): when present, it replaces JWKS discovery with a fixed symmetric
// key so tests validate a locally-signed token without a network call to a
// live Keycloak -- see GameApi.Tests.TestSupport.JwtTokenHelper.
var keycloakAuthority = builder.Configuration["Keycloak:Authority"];
var testSigningKey = builder.Configuration["Keycloak:TestSigningKey"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrEmpty(testSigningKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(testSigningKey)),
            };
        }
        else
        {
            options.Authority = keycloakAuthority;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            // Keycloak access tokens carry no audience for this API today (no
            // dedicated client/audience mapper) -- the "bga-game-history"
            // scope check below is what actually gates access.
            options.TokenValidationParameters = new TokenValidationParameters { ValidateAudience = false };
        }
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BgaGameHistory", policy => policy.RequireAssertion(
        ctx => BgaJwtAuth.HasScope(ctx.User, "bga-game-history")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// POST .../adjustment (below) is a privileged action -- correcting a
// player's recorded wins/losses -- so it's gated on its own dedicated API
// key rather than the "bga-game-history" scope every reader holds. Read
// access and "may rewrite results" are different rights.
var adjustmentApiKey = app.Configuration["ApiKeys:Adjustment"];

app.MapGet("/healthz", () => Results.Ok());

app.MapGet("/api/tournaments", (GameApiDbContext db, [AsParameters] TournamentsQuery query, CancellationToken cancellationToken) =>
    TournamentsHandler.IndexAsync(db, query, cancellationToken))
    .RequireAuthorization("BgaGameHistory");

app.MapGet("/api/tournaments/modes", (GameApiDbContext db, CancellationToken cancellationToken) =>
    TournamentsHandler.ModesAsync(db, cancellationToken))
    .RequireAuthorization("BgaGameHistory");

app.MapGet("/api/tournaments/{tournamentParentId:long}/players", (
    GameApiDbContext db, long tournamentParentId, CancellationToken cancellationToken) =>
    TournamentsHandler.PlayersAsync(db, tournamentParentId, cancellationToken))
    .RequireAuthorization("BgaGameHistory");

app.MapPost("/api/tournaments/{tournamentParentId:long}/players/{bgaUserId}/adjustment", async (
    HttpContext httpContext, GameApiDbContext db, long tournamentParentId, string bgaUserId,
    AdjustmentRequest? request, CancellationToken cancellationToken) =>
{
    const string bearerPrefix = "Bearer ";
    var header = httpContext.Request.Headers.Authorization.ToString();
    var presentedKey = header.StartsWith(bearerPrefix, StringComparison.Ordinal) ? header[bearerPrefix.Length..] : null;
    if (!ApiKeyAuth.Matches(presentedKey, adjustmentApiKey))
    {
        return Results.Unauthorized();
    }

    return await AdjustmentHandler.HandleAsync(db, tournamentParentId, bgaUserId, request, cancellationToken);
});

app.Run();

// Makes the top-level statements' implicit Program class public so
// WebApplicationFactory<Program> can host it from GameApi.Tests.
public partial class Program;
