using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Folio.Infrastructure.Persistence;

// Lets the `dotnet ef` tooling construct the context at design time (for migrations) without
// booting the API host.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FolioDbContext>
{
    public FolioDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FolioDbContext>()
            .UseSqlite("Data Source=folio.db")
            .Options;
        return new FolioDbContext(options);
    }
}
