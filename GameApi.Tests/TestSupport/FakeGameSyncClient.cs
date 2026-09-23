using GameApi.GameSync;

namespace GameApi.Tests.TestSupport;

/// <summary>
/// A scripted IGameSyncClient: pages are enqueued in advance and handed back
/// FIFO regardless of the `since`/`pageSize` a caller passes, so tests can
/// drive ConsolidationPass without a real HTTP call.
/// </summary>
public sealed class FakeGameSyncClient : IGameSyncClient
{
    private readonly Queue<GameSyncPage> _pages = new();

    public List<(DateTimeOffset? Since, int PageSize)> Requests { get; } = [];

    public FakeGameSyncClient Enqueue(GameSyncPage page)
    {
        _pages.Enqueue(page);
        return this;
    }

    /// <summary>Convenience for the common single-page, no-more-to-read case.</summary>
    public FakeGameSyncClient EnqueueOnePage(params GameSyncGame[] games)
    {
        var nextSince = games.Length > 0 ? games[^1].ReceivedAt.ToString("O") : null;
        return Enqueue(new GameSyncPage(games, nextSince, HasMore: false));
    }

    public Task<GameSyncPage> FetchPageAsync(DateTimeOffset? since, int pageSize, CancellationToken cancellationToken = default)
    {
        Requests.Add((since, pageSize));

        if (_pages.Count == 0)
        {
            return Task.FromResult(new GameSyncPage([], since?.ToString("O"), HasMore: false));
        }

        return Task.FromResult(_pages.Dequeue());
    }
}
