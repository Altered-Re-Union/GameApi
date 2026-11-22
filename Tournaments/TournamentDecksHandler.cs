using System.Buffers.Text;
using System.Text.Json;
using GameApi.Data;
using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Tournaments;

/// <summary>
/// GET /api/tournament-decks -- the service-to-service feed DeckApi polls to
/// publish tournament decklists under the player's ReUnion identity. One item
/// per PlayerTournament row (including rows with no ReUnion link yet, so
/// DeckApi can see a link appear later), joined with its Tournament for
/// name/mode.
///
/// Keyset-paginated on (ComputedAt, TournamentParentId, BgaUserId) ascending:
/// ComputedAt only moves when a row's content actually changes (see
/// ConsolidationPass), so polling forever with the last `next` yields every
/// new or changed row exactly once per change, and the full composite key
/// means ties on ComputedAt (a whole tournament recomputed in one pass) never
/// skip or repeat a row across pages.
///
/// Deliberately omits BgaName (attribution is ReUnion-only) and the admin
/// W/L adjustments (per player, not per deck).
/// </summary>
public static class TournamentDecksHandler
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 500;

    public static async Task<IResult> HandleAsync(GameApiDbContext db, [AsParameters] TournamentDecksQuery query, CancellationToken cancellationToken)
    {
        var pageSize = query.PageSize < 1 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        FeedCursor? after = null;
        if (!string.IsNullOrEmpty(query.After))
        {
            after = FeedCursor.TryDecode(query.After);
            if (after is null)
            {
                return Results.BadRequest(new { error = "after is not a valid cursor." });
            }
        }

        var rows = db.PlayerTournaments.AsQueryable();
        if (after is { } c)
        {
            rows = rows.Where(p =>
                p.ComputedAt > c.ComputedAt
                || (p.ComputedAt == c.ComputedAt
                    && (p.TournamentParentId > c.TournamentParentId
                        || (p.TournamentParentId == c.TournamentParentId && string.Compare(p.BgaUserId, c.BgaUserId) > 0))));
        }

        var page = await (
                from p in rows
                join t in db.Tournaments on p.TournamentParentId equals t.TournamentParentId into tournaments
                from t in tournaments.DefaultIfEmpty()
                orderby p.ComputedAt, p.TournamentParentId, p.BgaUserId
                select new
                {
                    p.TournamentParentId,
                    TournamentName = t == null ? null : t.TournamentParentName,
                    Mode = t == null ? null : t.Mode,
                    p.BgaUserId,
                    p.ReunionUserId,
                    p.ReunionPseudo,
                    p.DecksJson,
                    p.ComputedAt,
                })
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var players = page.Select(p => new TournamentDeckPlayer(
                p.TournamentParentId,
                p.TournamentName,
                p.Mode,
                p.BgaUserId,
                p.ReunionUserId,
                p.ReunionPseudo,
                TournamentsHandler.DeckUsagesOf(p.DecksJson),
                p.ComputedAt))
            .ToList();

        // An empty page hands the caller's own cursor back, so "poll with the
        // last next" works forever, including before anything new exists.
        var next = page.Count > 0
            ? new FeedCursor(page[^1].ComputedAt, page[^1].TournamentParentId, page[^1].BgaUserId).Encode()
            : string.IsNullOrEmpty(query.After) ? null : query.After;

        return Results.Ok(new TournamentDecksResponse(players, next, hasMore));
    }

    /// <summary>
    /// Opaque to callers: base64url (no padding) of {"c":ComputedAt,"t":TournamentParentId,"u":BgaUserId}.
    /// ComputedAt is kept as a round-trip ("O") string so the exact stored
    /// value survives -- a lossy timestamp could skip or repeat tied rows.
    /// </summary>
    internal sealed record FeedCursor(DateTimeOffset ComputedAt, long TournamentParentId, string BgaUserId)
    {
        public string Encode() => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Wire(ComputedAt.ToUniversalTime().ToString("O"), TournamentParentId, BgaUserId)));

        public static FeedCursor? TryDecode(string value)
        {
            try
            {
                var wire = JsonSerializer.Deserialize<Wire>(Base64Url.DecodeFromChars(value));
                if (wire?.C is null || wire.U is null
                    || !DateTimeOffset.TryParse(wire.C, null, System.Globalization.DateTimeStyles.RoundtripKind, out var computedAt))
                {
                    return null;
                }

                // Npgsql only accepts UTC DateTimeOffset parameters for timestamptz.
                return new FeedCursor(computedAt.ToUniversalTime(), wire.T, wire.U);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private sealed record Wire(
            [property: System.Text.Json.Serialization.JsonPropertyName("c")] string? C,
            [property: System.Text.Json.Serialization.JsonPropertyName("t")] long T,
            [property: System.Text.Json.Serialization.JsonPropertyName("u")] string? U);
    }
}

public sealed record TournamentDecksQuery(string? After, int PageSize = TournamentDecksHandler.DefaultPageSize);

/// <summary>Decks are DeckfmtCodec-compressed; Pseudo is the ReUnion pseudo. No BgaName, no admin adjustments -- see TournamentDecksHandler.</summary>
public sealed record TournamentDeckPlayer(
    long TournamentParentId,
    string? TournamentName,
    string? Mode,
    string BgaUserId,
    string? ReunionUserId,
    string? Pseudo,
    IReadOnlyList<DeckUsage> Decks,
    DateTimeOffset ComputedAt);

public sealed record TournamentDecksResponse(IReadOnlyList<TournamentDeckPlayer> Players, string? Next, bool HasMore);
