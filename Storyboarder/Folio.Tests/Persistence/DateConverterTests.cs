using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Folio.Domain.Model;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Persistence;

/// <summary>
/// Every <c>DateTimeOffset</c> the database stores goes through a converter.
///
/// <para><b>SQLite has no native DateTimeOffset</b>, so the provider cannot order by one or compare
/// two — it throws <c>NotSupportedException</c> at the moment a query touches the column. Which
/// means a property added without its converter is not a build error, not a migration error, and not
/// a startup error: it is a crash on whichever screen first sorts by it. <c>ShelvedAtUtc</c> was
/// added that way and the shelf listing threw the first time it ran.</para>
///
/// <para>Read from the built model rather than from the mapping code, so it is about what the
/// database actually does and not about whether a line looks present.</para>
/// </summary>
public class DateConverterTests : IDisposable
{
    private readonly TestDatabase database = new();

    public void Dispose() => database.Dispose();

    [Fact]
    public void NoDateIsStoredWithoutOne()
    {
        using var context = database.NewContext();

        var unconverted = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.ClrType == typeof(DateTimeOffset)
                    || property.ClrType == typeof(DateTimeOffset?))
                .Where(property => property.GetValueConverter() is null)
                .Select(property => $"{entity.ClrType.Name}.{property.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(unconverted.Count == 0,
            "SQLite cannot order by or compare a DateTimeOffset, and throws where one is queried. "
            + "These are stored without a converter:\n" + string.Join("\n", unconverted));
    }

    /// <summary>
    /// And the converter has to survive a round trip, not merely exist — a date that comes back
    /// shifted is worse than one that throws, because nothing reports it.
    /// </summary>
    [Fact]
    public async Task AStoredDateComesBackUnchanged()
    {
        var when = new DateTimeOffset(2026, 9, 13, 19, 45, 30, TimeSpan.Zero);
        Guid id;

        using (var context = database.NewContext())
        {
            var novel = new Novel
            {
                Title = "Quest", GoogleDocId = "doc",
                CreatedAtUtc = when, UpdatedAtUtc = when, ShelvedAtUtc = when,
            };
            novel.ProjectId = novel.Id;
            context.Novels.Add(novel);
            await context.SaveChangesAsync();
            id = novel.Id;
        }

        using var check = database.NewContext();
        var stored = await check.Novels.FirstAsync(n => n.Id == id);

        Assert.Equal(when, stored.ShelvedAtUtc);
        Assert.Equal(when, stored.CreatedAtUtc);
    }

    // The failure this guards: ordering by the column is what threw.
    [Fact]
    public async Task TheShelfCanBeOrderedByWhenThingsWerePutOnIt()
    {
        using var context = database.NewContext();
        await context.Novels
            .Where(novel => novel.ShelvedAtUtc != null)
            .OrderByDescending(novel => novel.ShelvedAtUtc)
            .Select(novel => novel.Id)
            .ToListAsync();
    }
}
