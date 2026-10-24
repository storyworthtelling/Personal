using System.Text.RegularExpressions;
using Constitution.Domain;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// C-6.1.2 (G6) — the kind half of every household record's key, and the file's name. A kind is a
/// wire name: the file outlives every build that reads it, so a renamed kind is a reader that finds
/// nothing while the old records sit unread in Drive for good. The expected values are typed out
/// here on purpose; growing the set is a deliberate edit to this list.
/// </summary>
public class ConstitutionKindsTests
{
    private static readonly string[] ExpectedKinds =
    [
        "recipe",
        "week-plan",
        "rating",
        "taste-note",
        "staple",
        "person",
        "planning-lease",
    ];

    [Fact]
    public void KindsAreFrozen()
    {
        Assert.Equal("recipe", ConstitutionKinds.Recipe);
        Assert.Equal("week-plan", ConstitutionKinds.WeekPlan);
        Assert.Equal("rating", ConstitutionKinds.Rating);
        Assert.Equal("taste-note", ConstitutionKinds.TasteNote);
        Assert.Equal("staple", ConstitutionKinds.Staple);
        Assert.Equal("person", ConstitutionKinds.Person);
        Assert.Equal("planning-lease", ConstitutionKinds.PlanningLease);

        Assert.Equal(ExpectedKinds, ConstitutionKinds.All);
    }

    [Fact]
    public void FileNameIsFrozen()
    {
        Assert.Equal("storyworthtelling-constitution-household.json", ConstitutionIdentity.HouseholdFileName);
    }

    [Fact]
    public void TheSetIsNotEmptyOrRepeated()
    {
        Assert.NotEmpty(ConstitutionKinds.All);
        Assert.Equal(ConstitutionKinds.All.Count, ConstitutionKinds.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryKindIsAKeyThatSurvivesNormalising()
    {
        foreach (var kind in ConstitutionKinds.All)
        {
            Assert.Equal(kind, kind.ToLowerInvariant());
            Assert.Equal(kind, new RecordKey(kind, "x").Kind);
            Assert.Matches("^[a-z]+(-[a-z]+)*$", kind);
        }
    }

    [Fact]
    public void EveryKindIsReservedAndNothingElseIs()
    {
        Assert.All(ConstitutionKinds.All, kind => Assert.True(ConstitutionKinds.IsReserved(kind)));
        Assert.True(ConstitutionKinds.IsReserved("PERSON"));
        Assert.False(ConstitutionKinds.IsReserved("viewer"));
        Assert.False(ConstitutionKinds.IsReserved(""));
    }

    /// <summary>A kind spelled from an identifier renames itself with it; the whole file is scanned, comments aside.</summary>
    [Fact]
    public void KindsAreLiteralsNeverNameof()
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => ConstitutionSource.IsGuard(path, "ConstitutionKinds.cs"));
        Assert.True(file is not null, "ConstitutionKinds.cs was not found under Constitution/.");

        var code = Regex.Replace(File.ReadAllText(file!), @"///.*|//.*", string.Empty);
        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The library keeps what it does not know. A newer build's kind (or a reserved kind this build has no map
    /// for yet) is carried through an edit and a merge untouched.
    /// </summary>
    [Fact]
    public void AKindNobodyMapsSurvivesAnEditAndAMerge()
    {
        var future = HouseholdRecord.Write(
            new RecordKey("a-newer-kind", "one"), 3, new Dictionary<string, string> { ["field"] = "value" },
            RosterData.At(500), RosterData.PartnerWriter);
        var document = HouseholdDocument.Empty.With(future);

        var edited = new RosterEditor(RosterRole.Owner).Save(document, RosterData.Sam(), RosterData.At(1000), RosterData.OwnerWriter).Document;
        var merged = HouseholdMerge.Merge(edited, document);

        Assert.True(merged.Find(future.Key)!.ContentEquals(future));
        Assert.NotNull(merged.Live(new RecordKey(ConstitutionKinds.Person, "sam")));
    }
}
