using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CorporateRequestManagement.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DefaultLocalDbConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=CorporateRequestManagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? DefaultLocalDbConnection : connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
