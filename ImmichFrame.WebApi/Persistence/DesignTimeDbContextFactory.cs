using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Used only by the EF Core command-line tooling (e.g. "dotnet ef migrations add").
/// Providing this factory keeps design-time operations from executing the application's
/// startup pipeline. The connection string is irrelevant for generating migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=immichframe-design.db")
            .Options;

        return new AppDbContext(options);
    }
}
