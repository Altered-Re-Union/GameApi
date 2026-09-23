namespace GameApi.Data.Entities;

/// <summary>
/// Single-row watermark for GameSyncClient polling: the ReceivedAt of the
/// newest game this service has pulled from altered-bga-api's GET
/// /api/games. Same idea as altered-bga-api's own (now-deleted)
/// TournamentSnapshotCursor.
/// </summary>
public class SyncCursor
{
    public const string Key = "GameSync";

    public string Id { get; set; } = Key;

    /// <summary>Null means "read from the beginning" -- the very first pass.</summary>
    public DateTimeOffset? Since { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
