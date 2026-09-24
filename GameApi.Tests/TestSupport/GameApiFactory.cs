using GameApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameApi.Tests.TestSupport;

/// <summary>
/// Hosts the real Program.cs pipeline against an in-memory stand-in for
/// Postgres and a fixed JWT signing key, so auth tests exercise the actual
/// JwtBearer + authorization-policy wiring rather than a reimplementation of
/// it. The consolidation worker is disabled by default -- it would otherwise
/// poll a bga-api base URL that doesn't exist in tests.
/// </summary>
public sealed class GameApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    public string AdjustmentApiKey { get; } = "test-adjustment-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AlteredBgaApi:BaseUrl", "http://127.0.0.1:1/");
        builder.UseSetting("AlteredBgaApi:ApiKey", "test-bga-api-key");
        builder.UseSetting("Keycloak:TestSigningKey", JwtTokenHelper.TestSigningKey);
        builder.UseSetting("ApiKeys:Adjustment", AdjustmentApiKey);
        builder.UseSetting("Consolidation:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<GameApiDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<GameApiDbContext>>();
            services.AddDbContext<GameApiDbContext>(options => options.UseInMemoryDatabase(_dbName));
        });
    }
}
