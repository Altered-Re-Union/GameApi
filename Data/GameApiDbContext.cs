using GameApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Data;

public class GameApiDbContext(DbContextOptions<GameApiDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();

    public DbSet<PlayerGame> PlayerGames => Set<PlayerGame>();

    public DbSet<Tournament> Tournaments => Set<Tournament>();

    public DbSet<PlayerTournament> PlayerTournaments => Set<PlayerTournament>();

    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>(e =>
        {
            e.HasKey(x => x.TableId);
            e.Property(x => x.TableId).ValueGeneratedNever();
            e.HasIndex(x => x.ReceivedAt);
            e.HasIndex(x => x.TournamentParentId);
        });

        b.Entity<PlayerGame>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TableId, x.BgaUserId });
            e.HasOne(x => x.Game)
                .WithMany(g => g.PlayerGames)
                .HasForeignKey(x => x.TableId);
        });

        b.Entity<Tournament>(e =>
        {
            e.HasKey(x => x.TournamentParentId);
            // Never database-generated: the key is BGA's own parent tournament id.
            e.Property(x => x.TournamentParentId).ValueGeneratedNever();
        });

        b.Entity<PlayerTournament>(e =>
        {
            e.HasKey(x => new { x.TournamentParentId, x.BgaUserId });
        });

        b.Entity<SyncCursor>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}
