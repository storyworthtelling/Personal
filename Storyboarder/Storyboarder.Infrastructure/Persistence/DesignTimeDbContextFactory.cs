using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Storyboarder.Infrastructure.Persistence;

// Lets the `dotnet ef` tooling construct the context at design time (for migrations) without
// booting the API host.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StoryboarderDbContext>
{
    public StoryboarderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StoryboarderDbContext>()
            .UseSqlite("Data Source=storyboarder.db")
            .Options;
        return new StoryboarderDbContext(options);
    }
}
