using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameApi.GameSync;

/// <summary>Abstraction over GameSyncClient so Consolidation/ConsolidationPass is testable without a real HTTP call.</summary>
public interface IGameSyncClient
{
    Task<GameSyncPage> FetchPageAsync(DateTimeOffset? since, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pulls pages from altered-bga-api's GET /api/games. One HTTP call per
/// page; paging and cursor bookkeeping live in Consolidation/ConsolidationPass,
/// this is just the wire client.
/// </summary>
public sealed class GameSyncClient(HttpClient httpClient) : IGameSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GameSyncPage> FetchPageAsync(
        DateTimeOffset? since, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = $"/api/games?pageSize={pageSize}"
            + (since is { } value ? $"&since={Uri.EscapeDataString(value.ToString("O"))}" : "");

        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<GameSyncPage>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GET /api/games returned an empty body.");

        return page;
    }
}

/// <summary>Configures the named HttpClient this service uses to reach altered-bga-api.</summary>
public static class GameSyncClientRegistration
{
    public const string HttpClientName = "altered-bga-api";

    public static void Configure(HttpClient client, string baseUrl, string apiKey)
    {
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}

public sealed record GameSyncPage(IReadOnlyList<GameSyncGame> Games, string? NextSince, bool HasMore);

public sealed record GameSyncGame(
    long TableId,
    DateTimeOffset ReceivedAt,
    string? Format,
    long? TournamentId,
    string? TournamentName,
    long? TournamentParentId,
    string? TournamentGroup,
    IReadOnlyList<GameSyncPlayer> Players);

public sealed record GameSyncPlayer(
    string? BgaUserId,
    string? BgaName,
    bool IsWinner,
    string? Deck,
    string? PlayedCards);
