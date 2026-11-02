using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Infrastructure.Persistence;

// Used only by the EF Core tools (migrations and bundles). The connection string is read at
// design time from MARKETPLACE_DESIGN_DATABASE, or defaults to the local Compose database.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string _localDatabase = "Host=localhost;Port=5432;Database=marketplace;Username=marketplace;Password=devpassword";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_DESIGN_DATABASE") ?? _localDatabase;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
