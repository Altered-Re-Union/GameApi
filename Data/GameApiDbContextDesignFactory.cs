using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameApi.Data;

// Used only by `dotnet ef` at design time to generate migrations.
// At runtime the DbContext is configured from ConnectionStrings:gameapidb (see Program.cs).
public class GameApiDbContextDesignFactory : IDesignTimeDbContextFactory<GameApiDbContext>
{
    public GameApiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameApiDbContext>()
            .UseNpgsql("Host=localhost;Database=gameapidb;Username=postgres;Password=postgres")
            .Options;
        return new GameApiDbContext(options);
    }
}
