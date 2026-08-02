using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace CorporateRequestManagement.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    ApplicationDbContext context,
    DevelopmentDataSeeder seeder,
    IHostEnvironment environment)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Database initialization yalnız Development ortamında çalıştırılabilir.");
        }

        await context.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
