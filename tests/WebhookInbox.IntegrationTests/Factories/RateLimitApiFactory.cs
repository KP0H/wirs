using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebhookInbox.Infrastructure;

namespace WebhookInbox.IntegrationTests.Factories;

public class RateLimitApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var dict = new Dictionary<string, string?>
            {
                // Signatures (требуются, чтобы /inbox принимал запросы)
                ["Signatures:Sources:0:Source"] = "github",
                ["Signatures:Sources:0:Provider"] = "github",
                ["Signatures:Sources:0:Require"] = "true",
                ["Signatures:Sources:0:Secret"] = "gh_test_secret",

                // Rate limiting (жёсткий маленький лимит для теста)
                ["RateLimiting:DefaultRequestsPerMinute"] = "100",
                ["RateLimiting:Sources:0:Source"] = "github",
                ["RateLimiting:Sources:0:RequestsPerMinute"] = "2"
            };
            cfg.AddInMemoryCollection(dict!);
        });

        builder.ConfigureServices(services =>
        {
            // Заменяем DbContext на in-memory SQLite
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.RemoveAll<PooledDbContextFactory<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            var conn = new SqliteConnection("DataSource=:memory:");
            conn.Open();
            services.AddSingleton(conn);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(conn));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }
}
