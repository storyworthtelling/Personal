using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class CategoryCatalogCodecTests
{
    [Fact]
    public void RoundTrips_TheSeedCatalog()
    {
        var seed = CategoryCatalog.Seed();

        var decoded = CategoryCatalogCodec.Decode(CategoryCatalogCodec.Encode(seed));

        Assert.Equal(
            seed.Categories.Select(c => (c.Id, c.Name, c.Glyph, c.Order, c.Builtin)),
            decoded.Categories.Select(c => (c.Id, c.Name, c.Glyph, c.Order, c.Builtin)));
    }

    [Fact]
    public void RoundTrips_AnOwnerEditedCatalog()
    {
        // Rename a built-in, add an owner shelf, then reorder — the whole edited state must survive.
        var edited = CategoryCatalog.Seed()
            .WithRenamed(CategoryCatalog.Media, "Watch & Listen")
            .WithAdded("Reading & Reference", "glyph_reading")
            .Reordered([CategoryCatalog.Media, "reading-reference", CategoryCatalog.Finance]);

        var decoded = CategoryCatalogCodec.Decode(CategoryCatalogCodec.Encode(edited));

        Assert.Equal(
            edited.Categories.Select(c => (c.Id, c.Name, c.Glyph, c.Order, c.Builtin)),
            decoded.Categories.Select(c => (c.Id, c.Name, c.Glyph, c.Order, c.Builtin)));
        var reading = decoded.ById("reading-reference")!;
        Assert.False(reading.Builtin);
        Assert.Equal("Watch & Listen", decoded.ById(CategoryCatalog.Media)!.Name);
    }

    [Fact]
    public void AbsentPayload_DecodesToSeed()
    {
        // A fresh device (nothing stored) gets the nine built-ins.
        Assert.Equal(
            CategoryCatalog.Seed().Categories.Select(c => c.Id),
            CategoryCatalogCodec.Decode(null).Categories.Select(c => c.Id));
        Assert.Equal(
            CategoryCatalog.Seed().Categories.Select(c => c.Id),
            CategoryCatalogCodec.Decode("").Categories.Select(c => c.Id));
    }

    [Fact]
    public void GarbledPayload_DecodesToSeed()
    {
        // A corrupt store must fall back to the built-ins, never throw.
        Assert.Equal(
            CategoryCatalog.Seed().Categories.Select(c => c.Id),
            CategoryCatalogCodec.Decode("{ not json ]").Categories.Select(c => c.Id));
    }

    [Fact]
    public void ValidEmptyCatalog_IsPreserved_NotReseeded()
    {
        // The owner removed every shelf — a valid, intentional empty state that must NOT
        // silently repopulate with the seeds (that only happens when nothing was stored).
        var empty = new CategoryCatalog([]);

        var decoded = CategoryCatalogCodec.Decode(CategoryCatalogCodec.Encode(empty));

        Assert.Empty(decoded.Categories);
    }

    [Fact]
    public void CategoriesReturn_InOrder_RegardlessOfStoredSequence()
    {
        // Encode preserves order; even if a hand-edited payload lists shelves out of order,
        // the catalog ctor sorts by Order so the tray reads them consistently.
        const string outOfOrder =
            """[{"Id":"b","Name":"B","Glyph":"g","Order":1,"Builtin":false},{"Id":"a","Name":"A","Glyph":"g","Order":0,"Builtin":false}]""";

        var decoded = CategoryCatalogCodec.Decode(outOfOrder);

        Assert.Equal(new[] { "a", "b" }, decoded.Categories.Select(c => c.Id));
    }
}
