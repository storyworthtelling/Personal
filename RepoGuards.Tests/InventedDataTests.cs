using LogLoopReader;

namespace RepoGuards.Tests;

/// <summary>
/// Fixtures, mockups and docs hold invented data and nothing real (CLAUDE.md, "keep personal information
/// out"). This was Constitution's guard (C-12.2.2, G23); the privacy audit found the real address and
/// names had lived in Foreword's and Plate's tests, which nothing scanned, so it now reads every file.
///
/// <para><b>What it is, honestly.</b> A guard cannot know a real value; it catches the <i>shapes</i> a
/// real value takes: an email outside the reserved domains, a phone number outside the fictional ranges,
/// a street address, a real lender's name. The <i>people</i> check cannot be done over free text, so it
/// is limited to places where a name is structurally a name — a person-named member in a fixture, a plain
/// <c>Name</c> in a <c>Household*</c> file, and a roster written as <c>household (A, B, C)</c> in a doc —
/// and holds them to the invented household (Sam, Lee, Robin, Juniper). It does <b>not</b> catch a real
/// name in a sentence, a dish, a reading or a date; review does. Findings print file and line and never
/// the matched text, since the text is exactly what must not be republished.</para>
/// </summary>
public class InventedDataTests
{
    [Fact]
    public void NoRealLookingDataInTheRepository()
    {
        var result = GuardScan.Run(GuardScan.RepoRoot(), InventedShapes.Find);
        AssertScanned(result);

        Assert.True(result.Hits.Count == 0,
            "Something in the repository is shaped like real data; use invented values "
            + "(example.com, 555-01xx, Redwood Bank, Sam/Lee/Robin/Juniper):\n" + string.Join("\n", result.Hits));
    }

    // ---- what the repo-wide scan had to learn ----
    [Theory]
    [InlineData("mail@shop.example and a@b.test")]
    [InlineData("<abc123@mail.gmail.com> and family@group.calendar.google.com")]
    [InlineData("https://user@www.youtube.com/watch")]
    [InlineData("Co-Authored-By: Claude <noreply@anthropic.com>")]
    public void AReservedOrIdentifierAddressIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("a.cs", sample), finding => finding.Kind == InventedShapes.EmailKind);
    }

    [Theory]
    [InlineData("call 555-010-0199")]
    [InlineData("call +1 (555) 123-4567")]
    [InlineData("call +15551234567")]
    [InlineData("call +447700900123")]
    [InlineData("call +44 7700 900123")]
    public void TheUnassignedAreaCodeAndTheDramaRangeAreFictional(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("a.cs", sample), finding => finding.Kind == InventedShapes.PhoneKind);
    }

    [Theory]
    [InlineData("Name = \"Wells" + " Fargo\"")]
    [InlineData("Bank(\"PN" + "C\")")]
    [InlineData("the Am" + "ex card")]
    public void ARealLenderIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("Ledger/Ledger.Tests/X.cs", sample), finding => finding.Kind == InventedShapes.LenderKind);
    }

    [Theory]
    [InlineData("Name = \"Redwood Bank\", \"Cedar Hollow\", \"Bluebird Card\"")]
    [InlineData("[\"com.pnc.ecommerce.mobile\"] = Finance")]
    [InlineData("the Discover tab and an ally")]
    public void AnInventedLenderOrAnOrdinaryWordIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("a.cs", sample), finding => finding.Kind == InventedShapes.LenderKind);
    }

    [Theory]
    [InlineData("Pulp/Pulp.Domain/Household/HouseholdSync.cs", "/// <param name=\"Zone\">The viewer's zone.</param>")]
    [InlineData("Pulp/Pulp.Domain/People/HouseholdRoster.cs", "/// <paramref name=\"Viewer\"/>")]
    [InlineData("Ledger/Ledger.Tests/Household/HouseholdV1Fixture.cs", "\"Name\": \"Maple Ridge Mortgage\"")]
    [InlineData("Storyworthtelling.Household.Tests/Household.cs", "\"fields\":{\"child\":\"false\"}")]
    public void ADocCommentAnIndexAccountAndAFlagAreNotPeople(string path, string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find(path, sample), finding => finding.Kind == InventedShapes.PersonKind);
    }

    // ---- emails ----

    public static IEnumerable<object[]> BadEmails()
    {
        yield return ["someone@" + "gmail.com"];
        yield return ["first.last+tag@" + "mail.corp.org"];
        yield return ["owner@" + "example.com.evil.org"];
        yield return ["owner@" + "example.community"];
        yield return ["<team@" + "anthropic.com>"];
        yield return ["\"to\": \"sam@" + "outlook.com\""];
    }

    [Theory]
    [MemberData(nameof(BadEmails))]
    public void ARealLookingEmailIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("Fixtures/a.json", "x\n" + sample + "\n"),
            finding => finding.Kind == InventedShapes.EmailKind && finding.Line == 2);
    }

    [Theory]
    [InlineData("owner@example.com")]
    [InlineData("Sam.Lee+groceries@EXAMPLE.COM")]
    [InlineData("uses: actions/checkout@v4")]
    [InlineData("<PackageReference Include=\"X\" Version=\"1.2.3\" />")]
    [InlineData("pkg@1.2.3 and android:label=\"@string/app_name\"")]
    [InlineData("[Obsolete] @Override user@localhost")]
    public void AnInventedOrNonEmailIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("a.md", sample), finding => finding.Kind.StartsWith("email"));
    }

    // ---- phone numbers ----

    public static IEnumerable<object[]> BadPhones()
    {
        yield return ["call 212-867-" + "5309"];
        yield return ["call (212) 867-" + "5309"];
        yield return ["call 212.867." + "5309"];
        yield return ["call 1-800-867-" + "5309"];
        yield return ["call +1 212 867 " + "5309"];
        yield return ["call +1" + "2128675309"];
        yield return ["call +44 20 " + "7946 0958"];
        yield return ["call 867-" + "5309"];
        yield return ["call 555-" + "0200"];
        yield return ["call 212-555-" + "0200"];
    }

    [Theory]
    [MemberData(nameof(BadPhones))]
    public void ARealLookingPhoneNumberIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("notes.md", "x\n" + sample + "\n"),
            finding => finding.Kind == InventedShapes.PhoneKind && finding.Line == 2);
    }

    [Theory]
    [InlineData("call 555-0123")]
    [InlineData("call 555.0100")]
    [InlineData("call (212) 555-0142")]
    [InlineData("call 212-555-0199")]
    [InlineData("call +1 212 555 0123")]
    [InlineData("call +12125550100")]
    [InlineData("on 2026-10-17 at 11:30-12:45")]
    [InlineData("versionCode 608111608 and 1234567890 and 1.0.4521")]
    [InlineData("SDK 17.12.0, band 10, 22 dp, ratio 0.750")]
    [InlineData("run 200000 to 299999 on 10/17/2026")]
    [InlineData("C-12.2.2 and P13-1234 and #476F83")]
    public void AnInventedNumberOrNonPhoneIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("notes.md", sample), finding => finding.Kind.StartsWith("phone"));
    }

    // ---- street addresses ----

    [Theory]
    [InlineData("lives at 221 " + "Baker Street")]
    [InlineData("14 " + "Maple Ave, Springfield")]
    [InlineData("9 " + "Old Mill Lane")]
    [InlineData("77 " + "Sunset Boulevard")]
    public void ARealLookingStreetAddressIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("notes.md", "x\n" + sample), finding => finding.Kind == InventedShapes.StreetKind && finding.Line == 2);
    }

    [Theory]
    [InlineData("1 Example Street")]
    [InlineData("a quiet street or an Example Road")]
    [InlineData("3 Google Drive files and 12 Hours of Way")]
    [InlineData("Section 4 Roadmap")]
    public void ANonAddressIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("notes.md", sample), finding => finding.Kind == InventedShapes.StreetKind);
    }

    // ---- people, in structured fixtures only ----

    [Theory]
    [InlineData("Constitution.Tests/Household.cs", "public const string Cook = \"Quen\" + \"tin\";")]
    [InlineData("Constitution.Tests/Household.cs", "public static string Name => \"Dana\";")]
    [InlineData("Constitution.Tests/HouseholdFixtures.cs", "new Member(Name: \"Dana\")")]
    [InlineData("Constitution.Tests/Fixtures/roster.json", "{ \"child\": \"Maya\" }")]
    [InlineData("Constitution.Tests/Fixtures/Meals.cs", "var partner = \"Sam Rivera\";")]
    [InlineData("Constitution.Tests/Fixtures/roster.json", "{ \"FirstName\": \"Dana\" , \"x\": 1 }")]
    public void APersonOutsideTheInventedHouseholdIsReportedInAFixture(string path, string sample)
    {
        Assert.Contains(InventedShapes.Find(path, "x\n" + sample),
            finding => finding.Kind == InventedShapes.PersonKind && finding.Line == 2);
    }

    [Theory]
    [InlineData("Constitution.Tests/Household.cs", "public const string Cook = \"Sam\";")]
    [InlineData("Constitution.Tests/Household.cs", "public static string Name => \"Juniper\";")]
    [InlineData("Constitution.Tests/Household.cs", "Name = \"lee\", Partner = \"Robin\", Child = \"\"")]
    [InlineData("Constitution.Tests/Fixtures/roster.json", "{ \"child\": \"Juniper\", \"FileName\": \"a.json\" }")]
    [InlineData("Constitution.Tests/Guard/Other.cs", "var Name = \"Roast Chicken\"; Cook = \"Dana\";")]
    [InlineData("Constitution.Tests/Fixtures/Meals.cs", "Name = \"Roast Chicken\"; ApplicationName = \"Constitution\";")]
    public void TheInventedHouseholdAndNonFixturesAreLeftAlone(string path, string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find(path, sample), finding => finding.Kind.StartsWith("person"));
    }

    [Theory]
    [InlineData("The household (Sam, Lee, Quentin, Juniper) shares one fridge.")]
    [InlineData("the invented household (Sam,\n  Lee and Dana)")]
    public void ARosterInTheDocsWithAnOutsiderIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("notes.md", sample),
            finding => finding.Kind == InventedShapes.PersonKind);
    }

    [Theory]
    [InlineData("The household (Sam, Lee, Robin, Juniper) shares one fridge.")]
    [InlineData("household (Juniper, Robin and Lee)")]
    [InlineData("household (read-only on the partner's install, D74)")]
    [InlineData("the household (Foreword) is not a roster")]
    public void AnInventedRosterOrNonRosterInTheDocsIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("notes.md", sample), finding => finding.Kind.StartsWith("person"));
    }

    [Fact]
    public void ARosterIsOnlyReadInMarkdown()
    {
        Assert.Empty(InventedShapes.Find("Constitution.Tests/Notes.cs", "// household (Sam, Lee, Dana)"));
    }

    // ---- end to end, against a planted tree ----

    [Fact]
    public void PlantedBadFixturesAreReportedByFileAndLine()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Fixtures/contact.json", "{\n  \"email\": \"" + "dana@" + "gmail.com\",\n  \"phone\": \"212-867-" + "5309\"\n}\n");
            GuardScan.Plant(root, "Household.cs", "class Household\n{\n    public const string Cook = \"Dana\";\n}\n");
            GuardScan.Plant(root, "docs/plan.md", "# Plan\n\nhousehold (Sam, Lee, Robin, Juniper)\nowner@example.com 555-0142\n");
            GuardScan.Plant(root, "obj/ignored.json", "\"" + "dana@" + "gmail.com\"");

            var result = GuardScan.Run(root.FullName, InventedShapes.Find);

            Assert.Equal(["Fixtures/contact.json", "Household.cs", "docs/plan.md"], result.Scanned);
            Assert.Equal(
            [
                "Fixtures/contact.json:2: " + InventedShapes.EmailKind,
                "Fixtures/contact.json:3: " + InventedShapes.PhoneKind,
                "Household.cs:3: " + InventedShapes.PersonKind,
            ],
            result.Hits.Select(hit => hit.ToString()).OrderBy(line => line, StringComparer.Ordinal));
            Assert.DoesNotContain(result.Hits, hit => hit.ToString().Contains("dana", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }


    private static void AssertScanned(ScanResult result)
    {
        Assert.True(result.Scanned.Count >= 1000,
            $"InventedDataTests scanned {result.Scanned.Count} files; the guard would pass on nothing.");

        foreach (var file in RepoDocs.Named)
        {
            Assert.Contains(file, result.Scanned);
        }
    }
}
