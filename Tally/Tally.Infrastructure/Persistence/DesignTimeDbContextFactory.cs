using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tally.Infrastructure.Persistence;

// Lets the `dotnet ef` tooling construct the context at design time (for migrations) without booting
// the Android head.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TallyDbContext>
{
    public TallyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TallyDbContext>()
            .UseSqlite("Data Source=tally.db")
            .Options;
        return new TallyDbContext(options);
    }
}
