using System.Reflection;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// C-6.1.4 (D74) — the household roster has one writer. The owner's install edits it; the partner's
/// install reads it and is refused, with a reason it can say. Birthdates live only in the encrypted
/// record's own field and never reach a list, a label or a Diagnostics line.
///
/// <para><b>Accepted limit, stated:</b> the install's role comes from the household file's owner marker
/// (the shared vault's owner account, read through the library's <see cref="HouseholdFileRole"/>), so it is
/// unknown on a phone that has not yet had its passphrase restore the vault and nothing is remembered for
/// its account. Unknown is treated as Pulp treats it: the owner's rights, because a phone with no vault must
/// be able to set its own household up. The window is closed by reading the vault at launch and
/// remembering the role per account (<see cref="HouseholdRoleMemory"/>).</para>
/// </summary>
public class RosterTests
{
    private static readonly RosterEditor Owner = new(RosterRole.Owner);
    private static readonly RosterEditor Partner = new(RosterRole.Partner);

    [Fact]
    public void PartnerInstallIsReadOnly()
    {
        var before = RosterData.Household();

        var save = Partner.Save(before, RosterData.Sam() with { Name = "Samantha" }, RosterData.At(5000), RosterData.PartnerWriter);
        var remove = Partner.Remove(before, new PersonId("robin"), RosterData.At(5001), RosterData.PartnerWriter);

        foreach (var result in new[] { save, remove })
        {
            Assert.False(result.Accepted);
            Assert.Equal(RosterRefusalReason.PartnerInstallIsReadOnly, result.Refusal!.Reason);
            Assert.False(string.IsNullOrWhiteSpace(result.Refusal.Sentence));
            Assert.Same(before, result.Document);
        }

        Assert.Contains("Sam", Names(RosterView.From(before)));
        Assert.DoesNotContain("Samantha", Names(RosterView.From(before)));
        Assert.False(Partner.CanEdit);
    }

    [Fact]
    public void ThePartnerRefusalSaysWhoMayChangeIt()
    {
        var sentence = Partner.Save(RosterData.Household(), RosterData.Lee(), RosterData.At(5000), RosterData.PartnerWriter).Refusal!.Sentence;

        Assert.Contains("owner", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sentence, Partner.ReadOnlyNote);
        Assert.Null(Owner.ReadOnlyNote);
    }

    [Fact]
    public void ThePartnerStillReadsTheWholeRoster()
    {
        var view = RosterView.From(RosterData.Household());

        Assert.Equal(["Juniper", "Lee", "Robin", "Sam"], Names(view).Order(StringComparer.Ordinal));
        Assert.Equal(2, view.Adults.Count);
        Assert.Equal(2, view.Children.Count);
    }

    [Theory]
    [InlineData(HouseholdFileRole.Owner, RosterRole.Owner)]
    [InlineData(HouseholdFileRole.SharedWith, RosterRole.Partner)]
    [InlineData(HouseholdFileRole.Unknown, RosterRole.Owner)]
    public void TheRoleFollowsTheFilesOwnerMarker(HouseholdFileRole file, RosterRole expected)
    {
        Assert.Equal(expected, RosterEditor.RoleFor(file));
    }

    [Fact]
    public void TheOwnerSavesAPersonAsARecord()
    {
        var result = Owner.Save(HouseholdDocument.Empty, RosterData.Sam(), RosterData.At(1000), RosterData.OwnerWriter);

        Assert.True(result.Accepted);
        var record = result.Document.Live(new RecordKey(ConstitutionKinds.Person, "sam"));
        Assert.NotNull(record);
        Assert.Equal(RosterData.OwnerWriter, record.Writer);
        Assert.Equal(new HlcTimestamp(1000, 0), record.Stamp);
    }

    [Fact]
    public void ARenameMovesNoRecord()
    {
        var household = RosterData.Household();

        var renamed = Owner.Save(household, RosterData.Lee() with { Name = "Leigh" }, RosterData.At(6000), RosterData.OwnerWriter).Document;

        var view = RosterView.From(renamed);
        Assert.Contains("Leigh", Names(view));
        Assert.DoesNotContain("Lee", Names(view));
        Assert.Equal(4, view.Entries.Count);
    }

    [Fact]
    public void RemovingLeavesATombstoneNotAGap()
    {
        var household = RosterData.Household();

        var after = Owner.Remove(household, new PersonId("robin"), RosterData.At(7000), RosterData.OwnerWriter).Document;

        Assert.DoesNotContain("Robin", Names(RosterView.From(after)));
        Assert.True(after.Find(new RecordKey(ConstitutionKinds.Person, "robin"))!.IsTombstone);
        // The older live copy cannot come back on a merge.
        Assert.DoesNotContain("Robin", Names(RosterView.From(HouseholdMerge.Merge(after, household))));
    }

    [Fact]
    public void AnEditKeepsAFieldOnlyANewerWriterKnows()
    {
        var newer = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Person, "sam"), 7,
            new Dictionary<string, string> { ["person"] = "sam", ["name"] = "Sam", ["role"] = "adult", ["nickname"] = "kept" },
            RosterData.At(100), RosterData.PartnerWriter);
        var document = HouseholdDocument.Empty.With(newer);

        var saved = Owner.Save(document, RosterData.Sam(), RosterData.At(2000), RosterData.OwnerWriter).Document;

        var record = saved.Live(newer.Key)!;
        Assert.Equal("kept", record.Fields["nickname"]);
        Assert.Equal(7, record.SchemaVersion);
    }

    [Fact]
    public void PickupIsOnlyEverAnAdultsAndAChildCannotHaveIt()
    {
        Assert.Throws<ArgumentException>(() =>
            new RosterPerson(new PersonId("kid"), "Kid", MemberRole.Child, null, onPickup: true, googleAddress: null));

        var view = RosterView.From(RosterData.Household());
        Assert.Equal(["Sam"], view.PickupAdults.Select(entry => entry.Name));
    }

    [Fact]
    public void AChildMadeOnPickupByWithIsRefusedAtSave()
    {
        var child = RosterData.Robin() with { OnPickup = true };

        Assert.Throws<ArgumentException>(() => Owner.Save(HouseholdDocument.Empty, child, RosterData.At(1), RosterData.OwnerWriter));
        Assert.Throws<ArgumentException>(() => RosterMaps.Person.FieldsOf(child));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANameIsRequired(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new RosterPerson(new PersonId("x"), name, MemberRole.Adult, null, onPickup: false, googleAddress: null));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@example.com")]
    [InlineData("sam@")]
    [InlineData("a@b@example.com")]
    public void AGoogleAddressMustLookLikeOne(string address)
    {
        Assert.Throws<ArgumentException>(() =>
            new RosterPerson(new PersonId("x"), "X", MemberRole.Adult, null, onPickup: false, googleAddress: address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sam")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public void AnIdIsLowerCaseWordsAndDashes(string id)
    {
        Assert.Throws<ArgumentException>(() => new PersonId(id));
    }

    [Fact]
    public void BirthdatesNeverRender()
    {
        var household = RosterData.Household();
        var view = RosterView.From(household);
        var rendered = new List<string> { view.ToString()! };

        foreach (var entry in view.Entries)
        {
            rendered.Add(entry.ToString());
            rendered.Add(RosterLabel.For(entry));
            rendered.AddRange(entry.GetType().GetProperties()
                .Where(property => property.PropertyType == typeof(string))
                .Select(property => (string?)property.GetValue(entry) ?? string.Empty));
        }

        var people = HouseholdRecords.Read(household, RosterMaps.Person);
        rendered.AddRange(people.Items.Select(person => person.ToString()));
        rendered.AddRange(people.Items.Select(person => $"{person} {person.Birthdate} {person.Birthdate?.ToString()}"));

        Assert.NotEmpty(rendered);
        foreach (var text in rendered)
        {
            Assert.DoesNotContain(RosterData.FakeYear, text, StringComparison.Ordinal);
        }

        // The projection is a type with no birthdate in it at all, not one that hides it.
        Assert.DoesNotContain(typeof(RosterEntry).GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            member => member.Name.Contains("birth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ALabelNamesWhoTheyAreAndNothingPrivate()
    {
        var entries = RosterView.From(RosterData.Household()).Entries.ToDictionary(entry => entry.Name);

        Assert.Equal("Sam, adult, on pickup", RosterLabel.For(entries["Sam"]));
        Assert.Equal("Lee, adult", RosterLabel.For(entries["Lee"]));
        Assert.Equal("Juniper, child", RosterLabel.For(entries["Juniper"]));
    }

    [Fact]
    public void ABadRecordIsReportedButNeverEchoesItsBirthdate()
    {
        var bad = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Person, "ghost"), 1,
            new Dictionary<string, string>
            {
                ["person"] = "ghost", ["name"] = "Ghost", ["role"] = "adult", ["pickup"] = "false", ["birthdate"] = "1900-13-45",
            },
            RosterData.At(10), RosterData.OwnerWriter);

        var view = RosterView.From(HouseholdDocument.Empty.With(bad));

        Assert.Empty(view.Entries);
        var unreadable = Assert.Single(view.Unreadable);
        Assert.Contains("birthdate", unreadable.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(RosterData.FakeYear, unreadable.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("45", unreadable.Reason, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Names(RosterView view) => view.Entries.Select(entry => entry.Name);
}
