using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebhookInbox.Infrastructure;

public class WebAppFactoryWithInMemoryIdem : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Replace DbContext with SQLite in-memory
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.RemoveAll<PooledDbContextFactory<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            conn.Open();
            services.AddSingleton(conn);
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(conn));

            // Replace idempotency store with InMemory
            var idemDesc = services.SingleOrDefault(d => d.ServiceType == typeof(WebhookInbox.Api.Idempotency.IIdempotencyStore));
            if (idemDesc is not null) services.Remove(idemDesc);
            services.AddSingleton<WebhookInbox.Api.Idempotency.IIdempotencyStore, WebhookInbox.Api.Idempotency.InMemoryIdempotencyStore>();

            // Ensure DB
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }
}
