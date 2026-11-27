using System.Text;
using Codex.Infrastructure.Dash;
using Watch.Domain;
using Watch.Domain.Dash;
using Xunit;

namespace Codex.Tests.Dash;

/// <summary>
/// Post's half of the Rolodex on the wrist (P14 B6, W-5.1.2; PC6): the whole of Post's own Rolodex, a page at a time,
/// each contact its name and a digest id and never a number, an address or a photo (the owner's ruling); and the way back,
/// from a contact's id to the number a new text goes to, found on the phone. Invented people and numbers.
/// </summary>
public class RolodexRelayTests
{
    private static RolodexPerson Person(long id, string name, params string[] phones) => new(id, name, phones);

    private static readonly RolodexPerson Lee = Person(7, "Lee", "+1 212-555-0101");
    private static readonly RolodexPerson Robin = Person(8, "Robin", "2125550102", "2125550112");
    private static readonly RolodexPerson Sam = Person(9, "Sam", "(212) 555-0103");

    private static string IdOf(long contact) => DashIds.Contact(contact.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static IEnumerable<DashRolodex> Pages(RolodexRelay relay) =>
        Enumerable.Range(0, relay.Pages).Select(page => relay.Page(page)!);

    [Fact]
    public void EveryoneWithANumberIsOnTheWrist()
    {
        var page = new RolodexRelay([Lee, Robin, Sam]).Page(0)!;

        Assert.Equal(["Lee", "Robin", "Sam"], page.Contacts.Select(contact => contact.Name));
        Assert.Equal((0, 1), (page.Page, page.Pages));
    }

    [Fact]
    public void AContactIsANameAndAnId()
    {
        var page = new RolodexRelay([Lee]).Page(0)!;

        Assert.Equal(new DashContact(IdOf(7), "Lee"), Assert.Single(page.Contacts));
        Assert.True(DashIds.IsContact(page.Contacts[0].Id));
    }

    [Fact]
    public void NoNumberCrosses()
    {
        // Every page as it would cross, read for every number and address the book holds, in every spelling.
        var people = new[]
        {
            Lee, Robin, Sam,
            Person(10, "Juniper’s school", "+1 (212) 555-0104", "212.555.0114"),
            Person(11, "Lee Robinson", "2125550105"),
        };
        var wire = string.Join("\n", Pages(new RolodexRelay(people)).Select(page => Encoding.UTF8.GetString(DashCodec.Encode(page))));

        foreach (var number in people.SelectMany(person => person.Phones))
        {
            var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
            Assert.DoesNotContain(digits[^7..], wire, StringComparison.Ordinal);
            Assert.DoesNotContain(number, wire, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("@", wire, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(212) 555-0104")]
    [InlineData("+1 212 555 0104")]
    [InlineData("2125550104")]
    [InlineData("robin@example.com")]
    [InlineData("Robin 212-555-0104")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("…")]
    public void ANameThatIsNoNameIsLeftOut(string name)
    {
        // A contact with no name of its own is named by the phone after its number or address: that is never a name the
        // wrist may hold, so the contact is left out (and cannot be searched for by voice anyway).
        var relay = new RolodexRelay([Person(12, name, "2125550104"), Lee]);

        Assert.Equal(["Lee"], relay.Page(0)!.Contacts.Select(contact => contact.Name));
        Assert.Null(relay.NumberOf(IdOf(12)));
    }

    [Fact]
    public void ANameMayHoldAYear() =>
        Assert.Equal(["Class of 2026"], new RolodexRelay([Person(13, "Class of 2026", "2125550106")]).Page(0)!.Contacts.Select(contact => contact.Name));

    [Fact]
    public void AContactWithNoNumberIsLeftOut()
    {
        var relay = new RolodexRelay([Person(14, "Sam at work"), Person(15, "Voicemail", "*86"), Lee]);

        Assert.Equal(["Lee"], relay.Page(0)!.Contacts.Select(contact => contact.Name));
    }

    [Fact]
    public void TheOrderIsPosts() =>
        Assert.Equal(["Sam", "Lee", "Robin"], new RolodexRelay([Sam, Lee, Robin]).Page(0)!.Contacts.Select(contact => contact.Name));

    [Fact]
    public void AContactListedTwiceIsOnce() =>
        Assert.Equal(["Lee", "Robin"], new RolodexRelay([Lee, Robin, Lee]).Page(0)!.Contacts.Select(contact => contact.Name));

    [Fact]
    public void PagesAreAtMostTheirSize()
    {
        var relay = new RolodexRelay(Book(450));

        Assert.Equal(3, relay.Pages);
        Assert.Equal([200, 200, 50], Pages(relay).Select(page => page.Contacts.Count));
        Assert.All(Pages(relay), page => Assert.Equal(3, page.Pages));
        Assert.Equal(Book(450).Select(person => IdOf(person.ContactId)), Pages(relay).SelectMany(page => page.Contacts).Select(contact => contact.Id));
    }

    [Fact]
    public void AnEmptyBookIsOnePage()
    {
        var page = new RolodexRelay([]).Page(0)!;

        Assert.Equal((0, 1), (page.Page, page.Pages));
        Assert.Empty(page.Contacts);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(20)]
    public void APagePastTheLastIsNone(int page) => Assert.Null(new RolodexRelay(Book(450)).Page(page));

    [Fact]
    public void EveryPageNamesTheSameEdition()
    {
        var editions = Pages(new RolodexRelay(Book(450))).Select(page => page.Edition).Distinct().ToList();

        Assert.True(DashIds.IsEdition(Assert.Single(editions)));
    }

    [Fact]
    public void TheEditionMovesWithTheBook()
    {
        var edition = new RolodexRelay([Lee, Robin]).Page(0)!.Edition;

        Assert.Equal(edition, new RolodexRelay([Lee, Robin]).Page(0)!.Edition);
        Assert.NotEqual(edition, new RolodexRelay([Lee, Robin with { Name = "Robin Lee" }]).Page(0)!.Edition);
        Assert.NotEqual(edition, new RolodexRelay([Lee, Robin, Sam]).Page(0)!.Edition);
        // Only what the wrist is shown makes the edition: a changed number shows nothing new, and is found again at send time.
        Assert.Equal(edition, new RolodexRelay([Lee, Robin with { Phones = ["2125550122"] }]).Page(0)!.Edition);
    }

    [Fact]
    public void AtMostTwentyPages()
    {
        var relay = new RolodexRelay(Book(DashLimits.RolodexPages * DashLimits.RolodexPage + 50));

        Assert.Equal(DashLimits.RolodexPages, relay.Pages);
        Assert.Equal(DashLimits.RolodexPage, relay.Page(DashLimits.RolodexPages - 1)!.Contacts.Count);
    }

    [Fact]
    public void ANameIsOneLineAndFits()
    {
        var relay = new RolodexRelay([Person(16, "Juniper’s\nschool   team", "2125550107"), Person(17, new string('e', DashLimits.Name + 10), "2125550108")]);
        var names = relay.Page(0)!.Contacts.Select(contact => contact.Name).ToList();

        Assert.Equal("Juniper’s school team", names[0]);
        Assert.Equal(DashLimits.Name, names[1].Length);
        Assert.EndsWith("…", names[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ANameKeepsNoControlCharacters()
    {
        var name = new RolodexRelay([Person(18, "Ro\u0001bin\uD800", "2125550109")]).Page(0)!.Contacts.Single().Name;

        Assert.Equal("Robin", name);
    }

    [Fact]
    public void TheFullestPageFitsTheBudget()
    {
        // Three bytes a character at every name's limit, and characters the codec would escape six bytes each taken out.
        var people = Enumerable.Range(0, DashLimits.RolodexPage)
            .Select(index => Person(index + 1, new string('漢', DashLimits.Name - 2) + "\u0007\u0008\"", "2125550110"));
        var page = new RolodexRelay(people.ToList()).Page(0)!;

        var bytes = DashCodec.Encode(page);

        Assert.True(bytes.Length <= DashLimits.MaxBytes, $"{bytes.Length} bytes");
        Assert.True(DashCodec.Decode(WatchIdentity.DashRolodexPath, bytes).Accepted);
    }

    // ---- the way back ----

    [Fact]
    public void ANewTextGoesToTheContactsFirstNumber()
    {
        // Post's Rolodex lists a contact's numbers primary first, then a mobile (RolodexNumbers); the first that is a number.
        var relay = new RolodexRelay([Robin, Person(19, "Sam", "*86", "+1 212-555-0103")]);

        Assert.Equal("2125550102", relay.NumberOf(IdOf(8)));
        Assert.Equal("+1 212-555-0103", relay.NumberOf(IdOf(19)));
    }

    [Fact]
    public void AnUnknownContactHasNoNumber()
    {
        var relay = new RolodexRelay([Lee]);

        Assert.Null(relay.NumberOf(IdOf(99)));
        Assert.Null(relay.NumberOf(DashIds.Correspondence("2125550101")));
    }

    [Fact]
    public void TheSameContactKeepsItsId() =>
        Assert.Equal(new RolodexRelay([Lee]).Page(0)!.Contacts[0].Id, new RolodexRelay([Sam, Lee]).Page(0)!.Contacts[1].Id);

    private static IReadOnlyList<RolodexPerson> Book(int count) =>
        Enumerable.Range(1, count).Select(index => Person(index, $"Contact {index:D4}", "2125550111")).ToList();
}
