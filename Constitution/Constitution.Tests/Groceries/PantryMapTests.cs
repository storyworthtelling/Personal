using Constitution.Domain.Groceries;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;
using static Constitution.Tests.Groceries.GroceryData;

namespace Constitution.Tests.Groceries;

/// <summary>The typed map for a <c>staple</c> record: frozen field names and words, and what an unreadable one does.</summary>
public class PantryMapTests
{
    private static HouseholdDocument PutOne(Staple staple, long at = 1) =>
        HouseholdRecords.Put(HouseholdDocument.Empty, PantryMaps.Staple, staple, RecipeData.At(at), RecipeData.OwnerWriter);

    private static HouseholdDocument WriteRaw(string id, params (string Name, string Value)[] fields) =>
        HouseholdDocument.Empty.With(HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Staple, id),
            RecordSchema.Version,
            fields.ToDictionary(field => field.Name, field => field.Value),
            RecipeData.At(1),
            RecipeData.OwnerWriter));

    [Fact]
    public void TheKindIsStaple()
    {
        Assert.Equal("staple", PantryMaps.Staple.Kind);
        Assert.Equal(ConstitutionKinds.Staple, PantryMaps.Staple.Kind);
    }

    [Fact]
    public void TheKeyIsTheIngredientNotItsSpelling()
    {
        var key = PantryMaps.Staple.KeyOf(OnHand("Olive Oil", Aisle.BakingAndSpices, standing: true));

        Assert.Equal(key, PantryMaps.Staple.KeyOf(OnHand("  olive   oil ", Aisle.Other)));
        Assert.StartsWith("staple/", key.Value, StringComparison.Ordinal);
        Assert.NotEqual(key, PantryMaps.Staple.KeyOf(OnHand("olive", Aisle.Other)));
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        var names = PantryMaps.Staple.FieldsOf(Out("Olive oil", Aisle.BakingAndSpices)).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["aisle", "need", "staple", "stock"], names);
    }

    [Fact]
    public void TheStoredValuesAreFrozen()
    {
        var out1 = PantryMaps.Staple.FieldsOf(Out("Olive oil", Aisle.BakingAndSpices));
        var stock = PantryMaps.Staple.FieldsOf(OnHand("spaghetti", Aisle.DryGoods));

        Assert.Equal("Olive oil", out1["staple"]);
        Assert.Equal("baking-spices", out1["aisle"]);
        Assert.Equal("true", out1["need"]);
        Assert.Equal("false", out1["stock"]);
        Assert.Equal("false", stock["need"]);
        Assert.Equal("true", stock["stock"]);
        Assert.Equal("dry-goods", stock["aisle"]);
    }

    [Fact]
    public void EveryFieldRoundTrips()
    {
        foreach (var aisle in Aisles.All)
        {
            foreach (var staple in new[] { Out("Paper towels", aisle), OnHand("Flour", aisle, standing: true), OnHand("Leftover rice", aisle) })
            {
                var read = Assert.Single(HouseholdRecords.Read(PutOne(staple), PantryMaps.Staple).Items);

                Assert.Equal(staple, read);
            }
        }
    }

    [Fact]
    public void AMissingStockFlagMeansAStandingStaple()
    {
        var document = WriteRaw("honey", ("staple", "Honey"), ("aisle", "baking-spices"));

        var read = Assert.Single(HouseholdRecords.Read(document, PantryMaps.Staple).Items);

        Assert.True(read.Standing);
        Assert.False(read.NeedMore);
    }

    [Fact]
    public void AnAisleThisBuildDoesNotKnowIsUnreadable()
    {
        var document = WriteRaw("honey", ("staple", "Honey"), ("aisle", "gourmet-wing"));

        var result = HouseholdRecords.Read(document, PantryMaps.Staple);

        Assert.Empty(result.Items);
        var unreadable = Assert.Single(result.Unreadable);
        Assert.Contains("aisle", unreadable.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamelessOrBadlyFlaggedRecordIsUnreadable()
    {
        var nameless = HouseholdRecords.Read(WriteRaw("x", ("aisle", "other")), PantryMaps.Staple);
        var badFlag = HouseholdRecords.Read(WriteRaw("y", ("staple", "Salt"), ("aisle", "other"), ("need", "maybe")), PantryMaps.Staple);

        Assert.Single(nameless.Unreadable);
        Assert.Single(badFlag.Unreadable);
    }

    [Fact]
    public void AnEditThatChangesTheNeedKeepsTheRecord()
    {
        var document = PutOne(OnHand("Salt", Aisle.BakingAndSpices, standing: true));

        document = HouseholdRecords.Put(document, PantryMaps.Staple, Out("Salt", Aisle.BakingAndSpices), RecipeData.At(2), RecipeData.OwnerWriter);

        var read = Assert.Single(HouseholdRecords.Read(document, PantryMaps.Staple).Items);
        Assert.True(read.NeedMore);
    }
}
