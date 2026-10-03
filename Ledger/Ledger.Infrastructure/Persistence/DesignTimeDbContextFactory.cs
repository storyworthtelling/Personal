using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Index.Infrastructure.Persistence;

// Lets the `dotnet ef` tooling construct the context at design time (for migrations) without booting
// the Android head.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseSqlite("Data Source=ledger.db")
            .Options;
        return new LedgerDbContext(options);
    }
}
