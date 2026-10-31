using System.Text.RegularExpressions;

namespace Constitution.Tests.Guard;

/// <summary>
/// C-12.2.2 (G23) — fixtures and docs under <c>Constitution/</c> hold the book's invented
/// household and nothing real. The repository is public: every email address is at
/// <c>example.com</c>, every phone number is <c>555-01xx</c>, no street address appears, and the
/// people in a structured fixture are Sam, Lee, Robin and Juniper.
///
/// <para><b>What this is, honestly.</b> A guard cannot know a real value; it catches the
/// <i>shapes</i> a real value takes. The email, phone and street-address checks read every source,
/// config and markdown file. The <i>people</i> check cannot be done over free text — any capitalised
/// word might be a name, and an allow-list over prose would fail on every sentence — so it is limited
/// to places where a name is structurally a name: (1) literals assigned to person-named members
/// (<c>Cook</c>, <c>Child</c>, <c>Partner</c>, <c>FirstName</c>, ...) in fixture files — a file named
/// <c>Household*.cs|json</c> or anything under a <c>Fixtures</c> folder — plus a plain <c>Name</c> in a
/// <c>Household*</c> file, and (2) a roster written as <c>household (A, B, C)</c> in the markdown
/// docs. No such fixture exists yet (Release A has no Household class), so today the people check
/// is exercised by its self-tests and by planted files, and will start to bite the moment a
/// <c>Household</c> fixture lands. It does <b>not</b> catch a real name in a sentence, a dish, a
/// reading or a date; review does. Findings print file and line and never the matched text, since
/// the text is exactly what must not be republished.</para>
/// </summary>
public class InventedDataTests
{
    private const int FileFloor = 12;

    [Fact]
    public void NoRealLookingContactShapeUnderConstitution()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), InventedShapes.Find);
        AssertScanned(result);

        Assert.True(result.Hits.Count == 0,
            "Something under Constitution/ is shaped like real contact data; use the invented household "
            + "(example.com, 555-01xx, Sam/Lee/Robin/Juniper):\n" + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheScanCoversSourceConfigAndTheDocs()
    {
        AssertScanned(GuardScan.Run(ConstitutionSource.Root(), (path, text) => []));
    }

    // ---- emails ----

    public static IEnumerable<object[]> BadEmails()
    {
        yield return ["someone@" + "gmail.com"];
        yield return ["first.last+tag@" + "mail.corp.org"];
        yield return ["owner@" + "example.com.evil.org"];
        yield return ["owner@" + "example.community"];
        yield return ["<noreply@" + "anthropic.com>"];
        yield return ["\"to\": \"sam@" + "outlook.com\""];
    }

    [Theory]
    [MemberData(nameof(BadEmails))]
    public void ARealLookingEmailIsReported(string sample)
    {
        Assert.Contains(InventedShapes.Find("Fixtures/a.json", "x\n" + sample + "\n"),
            finding => finding.Kind == "email address outside example.com" && finding.Line == 2);
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
            finding => finding.Kind == "phone number outside 555-01xx" && finding.Line == 2);
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
        Assert.Contains(InventedShapes.Find("notes.md", "x\n" + sample), finding => finding.Kind == "street address" && finding.Line == 2);
    }

    [Theory]
    [InlineData("1 Example Street")]
    [InlineData("a quiet street or an Example Road")]
    [InlineData("3 Google Drive files and 12 Hours of Way")]
    [InlineData("Section 4 Roadmap")]
    public void ANonAddressIsLeftAlone(string sample)
    {
        Assert.DoesNotContain(InventedShapes.Find("notes.md", sample), finding => finding.Kind == "street address");
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
            finding => finding.Kind == "person outside the invented household" && finding.Line == 2);
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
            finding => finding.Kind == "person outside the invented household");
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
                "Fixtures/contact.json:2: email address outside example.com",
                "Fixtures/contact.json:3: phone number outside 555-01xx",
                "Household.cs:3: person outside the invented household",
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
        Assert.True(result.Scanned.Count >= FileFloor,
            $"InventedDataTests scanned {result.Scanned.Count} files (floor {FileFloor}); the guard would pass on nothing.");

        foreach (var extension in new[] { ".cs", ".csproj", ".md" })
        {
            Assert.Contains(result.Scanned, file => file.EndsWith(extension, StringComparison.Ordinal));
        }

        foreach (var doc in new[]
        {
            "CONSTITUTION-P13-PLAN.md",
            "CONSTITUTION-P13-DESIGN-NOTES.md",
            "CONSTITUTION-P13-PROGRESS.md",
            "CONSTITUTION-P13-TECHNICAL-FINDING.md",
            "spikes/nyt-recipes.md",
        })
        {
            Assert.Contains(doc, result.Scanned);
        }
    }
}

/// <summary>
/// Shapes of real contact data, found by line. Findings carry a line and a kind, never the matched
/// text. Deliberate limits are listed on <see cref="InventedDataTests"/>.
/// </summary>
internal static class InventedShapes
{
    private const string EmailKind = "email address outside example.com";
    private const string PhoneKind = "phone number outside 555-01xx";
    private const string StreetKind = "street address";
    private const string PersonKind = "person outside the invented household";

    /// <summary>The book's invented household, compared without case.</summary>
    private static readonly string[] Household = ["Sam", "Lee", "Robin", "Juniper"];

    /// <summary>
    /// local@domain, where the domain has a dot and ends in letters. The last rule is what leaves
    /// <c>pkg@1.2.3</c>, <c>actions/checkout@v4</c>, <c>@string/app_name</c> and <c>user@localhost</c> alone.
    /// </summary>
    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@(?<domain>[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,})");

    /// <summary>
    /// A North American number with separators: optional <c>+1</c>, optional area code, three digits,
    /// four digits. It may not start in the middle of a longer number, version or date.
    /// </summary>
    private static readonly Regex NorthAmericanPhone = new(
        @"(?<![0-9A-Za-z.\-+/#])(?:(?:\+?1[ .\-])?(?:\(\d{3}\)[ .\-]?|\d{3}[ .\-]))?(?<exchange>\d{3})[ .\-](?<line>\d{4})(?!\d)");

    /// <summary>A bare E.164 number: a plus sign and the country code and digits run together.</summary>
    private static readonly Regex E164Phone = new(@"(?<![0-9A-Za-z.\-+/#])\+(?<digits>\d{10,15})(?!\d)");

    /// <summary>An international number: a plus sign, a country code, then groups of digits split by spaces, dots or hyphens.</summary>
    private static readonly Regex InternationalPhone = new(
        @"(?<![0-9A-Za-z.\-+/#])\+(?<country>\d{1,3})(?:[ .\-]\d{1,5}){2,5}(?!\d)");

    /// <summary>
    /// A house number, one to three capitalised words, then a street suffix. The first word may not be
    /// the invented <c>Example</c> or a product name that happens to end the same way (<c>Google Drive</c>).
    /// </summary>
    private static readonly Regex StreetAddress = new(
        @"(?<![0-9.,:/#$\-])\b\d{1,5}\s+(?!(?:Example|Google|Shared|My|Team)\b)(?:[A-Z][a-z'\-]+\s+){1,3}?"
        + @"(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Court|Ct|Way|Place|Pl|Terrace|Highway|Hwy)\b");

    /// <summary>A literal assigned to a person-named member (C# or JSON), case-insensitive.</summary>
    private static readonly Regex PersonMember = new(
        @"(?i)(?<![A-Za-z0-9_])(?:Cook|Child|Children|Partner|Spouse|Parent|Kid|Person|Member|Owner|Guest|Eater|Diner|FirstName|GivenName|LastName|Surname|FamilyName)"
        + @"[""']?\s*(?:=>|=|:)\s*""(?<v>[^""\r\n]*)""");

    /// <summary>A plain <c>Name</c>, read only in a <c>Household*</c> file where it can only be a person.</summary>
    private static readonly Regex PlainName = new(
        @"(?i)(?<![A-Za-z0-9_])Name[""']?\s*(?:=>|=|:)\s*""(?<v>[^""\r\n]*)""");

    private static readonly Regex Roster = new(@"(?i)\bhousehold\s*\((?<list>[^)]*)\)");

    private static readonly Regex RosterSeparator = new(@",|;|&|\band\b");

    private static readonly Regex CapitalisedWord = new(@"^[A-Z][a-z]+$");

    public static IEnumerable<Finding> Find(string relativePath, string text)
    {
        var findings = new List<Finding>();
        FindEmails(text, findings);
        FindPhones(text, findings);
        FindStreets(text, findings);
        FindPeopleInFixtures(relativePath, text, findings);
        if (relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            FindRosters(text, findings);
        }

        return findings.Distinct().ToList();
    }

    private static void FindEmails(string text, List<Finding> findings)
    {
        foreach (Match match in Email.Matches(text))
        {
            if (!string.Equals(match.Groups["domain"].Value, "example.com", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), EmailKind));
            }
        }
    }

    private static void FindPhones(string text, List<Finding> findings)
    {
        foreach (Match match in NorthAmericanPhone.Matches(text))
        {
            // 555-01xx is the range reserved for fiction; an area code in front does not change that.
            var invented = match.Groups["exchange"].Value == "555" && match.Groups["line"].Value.StartsWith("01", StringComparison.Ordinal);
            if (!invented)
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), PhoneKind));
            }
        }

        foreach (Match match in E164Phone.Matches(text))
        {
            var digits = match.Groups["digits"].Value;
            var invented = digits.Length == 11 && digits.StartsWith('1')
                && digits.Substring(4, 3) == "555" && digits.Substring(7, 2) == "01";
            if (!invented)
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), PhoneKind));
            }
        }

        foreach (Match match in InternationalPhone.Matches(text))
        {
            // +1 numbers are judged by the North American rule above.
            if (match.Groups["country"].Value != "1")
            {
                findings.Add(new Finding(GuardScan.LineOf(text, match.Index), PhoneKind));
            }
        }
    }

    private static void FindStreets(string text, List<Finding> findings)
    {
        foreach (Match match in StreetAddress.Matches(text))
        {
            findings.Add(new Finding(GuardScan.LineOf(text, match.Index), StreetKind));
        }
    }

    /// <summary>A fixture is a <c>Household*.cs|json</c> file or anything under a <c>Fixtures</c> folder.</summary>
    private static bool IsFixture(string relativePath, out bool isHousehold)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        var file = parts[^1];
        isHousehold = file.StartsWith("Household", StringComparison.OrdinalIgnoreCase)
            && (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var underFixtures = parts.Take(parts.Length - 1).Any(part => string.Equals(part, "Fixtures", StringComparison.OrdinalIgnoreCase));
        return isHousehold || underFixtures;
    }

    private static void FindPeopleInFixtures(string relativePath, string text, List<Finding> findings)
    {
        if (!IsFixture(relativePath, out var isHousehold))
        {
            return;
        }

        var patterns = isHousehold ? new[] { PersonMember, PlainName } : new[] { PersonMember };
        foreach (var pattern in patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var value = match.Groups["v"].Value.Trim();
                if (value.Length > 0 && !IsHouseholdMember(value))
                {
                    findings.Add(new Finding(GuardScan.LineOf(text, match.Index), PersonKind));
                }
            }
        }
    }

    /// <summary>
    /// <c>household (A, B and C)</c> is a roster when it holds two or more single capitalised words;
    /// <c>household (Foreword)</c> and <c>household (two adults)</c> are not. A roster naming anyone
    /// outside the invented four is reported at the outsider's line.
    /// </summary>
    private static void FindRosters(string text, List<Finding> findings)
    {
        foreach (Match match in Roster.Matches(text))
        {
            var list = match.Groups["list"].Value;
            var names = RosterSeparator.Split(list).Select(token => token.Trim()).Where(token => token.Length > 0).ToList();
            if (names.Count < 2 || !names.All(name => CapitalisedWord.IsMatch(name)))
            {
                continue;
            }

            foreach (var name in names.Where(name => !IsHouseholdMember(name)))
            {
                var at = match.Groups["list"].Index + list.IndexOf(name, StringComparison.Ordinal);
                findings.Add(new Finding(GuardScan.LineOf(text, at), PersonKind));
            }
        }
    }

    private static bool IsHouseholdMember(string value) =>
        Household.Contains(value, StringComparer.OrdinalIgnoreCase);
}
