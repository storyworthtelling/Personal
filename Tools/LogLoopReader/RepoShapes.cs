using System.Globalization;
using System.Text.RegularExpressions;

namespace LogLoopReader;

/// <summary>One thing a shape detector found: the line and the kind. Never the matched text itself.</summary>
public sealed record ShapeFinding(int Line, string Kind);

/// <summary>
/// Shapes of real credentials, found by line. One copy for the whole repository: the repo-wide
/// <c>NoKeysTests</c> (RepoGuards.Tests) and CI's <c>leakscan --ci</c> both read it, so the two
/// cannot drift. The patterns are built so that a test name, a namespace, a path, an EF migration id
/// or a checksum cannot match them; each is exercised both ways in RepoGuards.Tests.
///
/// <para><b>Strict</b> is Constitution's original rule for a value assigned to a secret-named member:
/// anything that does not call itself invented is reported, short and word-like values included. The
/// rest of the repository has hundreds of honest stand-ins (<c>"test-key"</c>, <c>"tok"</c>), so outside
/// Constitution only a random-looking value is reported.</para>
/// </summary>
public static class KeyShapes
{
    private const string TokenChars = @"A-Za-z0-9+/=_\-";

    private static readonly (string Kind, Regex Pattern)[] Shapes =
    [
        ("Google API key", new(@"AIza[0-9A-Za-z_\-]{35}")),
        ("secret-key token", new(@"\bsk-[A-Za-z0-9_\-]{20,}")),
        ("GitHub token", new(@"\bgh[pousr]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{22,}")),
        ("AWS access key id", new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
        ("Slack token", new(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}")),
        ("private key block", new(@"-----BEGIN [A-Z0-9 ]+-----")),
        // A quoted run of 32+ hex digits: the shape of most REST API keys.
        ("quoted hex run", new(@"[""'](?<v>[0-9A-Fa-f]{32,})[""']")),
        // Bare hex is only a key when a key word is on the line; otherwise it is a checksum or a commit id.
        ("hex value near a key word", new(
            @"(?i)\b(?:key|token|secret|password|passphrase|credential)s?\b[^\r\n]{0,60}?(?<![0-9A-Za-z])(?<v>[0-9a-f]{32,})(?![0-9A-Za-z])")),
        // A quoted 40+ character token holding both letters and digits.
        ("quoted long token", new(
            @"[""'](?<v>(?=[" + TokenChars + @"]*[0-9])(?=[" + TokenChars + @"]*[A-Za-z])[" + TokenChars + @"]{40,})[""']")),
        // Unquoted (prose, markdown): 32+ characters with upper, lower and a digit, not part of a path or dotted name.
        ("long token", new(
            @"(?<![A-Za-z0-9+/=_.\-])(?<v>(?=[A-Za-z0-9_\-]*[0-9])(?=[A-Za-z0-9_\-]*[a-z])(?=[A-Za-z0-9_\-]*[A-Z])[A-Za-z0-9_\-]{32,})(?![A-Za-z0-9_\-])")),
    ];

    /// <summary>A literal assigned to a password/passphrase/secret/token/key name, in C#, JSON, YAML or markdown.</summary>
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b[A-Za-z_]*(?:password|passphrase|secret|apikey|api_key|token|privatekey)[""']?\s*(?::|=>?)\s*@?(?:""(?<v>[^""\r\n]*)""|'(?<v>[^'\r\n]*)'|`(?<v>[^`\r\n]*)`)");

    /// <summary>
    /// A value that says it is invented, or is a reference rather than a value. This is the escape
    /// hatch for tests that need a stand-in: call it "invented ...", "sample ...", "example ..." and
    /// the guard leaves it alone.
    /// </summary>
    private static readonly Regex StandIn = new(
        @"(?i)^$|^\s*<|^\$\{|^\{\{|^\$\(|^%|invented|example|sample|placeholder|dummy|fake|redacted|xxxx|your[-_ ]");

    /// <summary>An EF Core migration id: a 14-digit timestamp, an underscore, a name.</summary>
    private static readonly Regex MigrationId = new(@"^\d{14}_");

    public static IReadOnlyList<ShapeFinding> Find(string text, bool strict = false)
    {
        var findings = new List<ShapeFinding>();
        foreach (var (kind, pattern) in Shapes)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var value = match.Groups["v"].Success ? match.Groups["v"].Value : match.Value;
                if (!LooksLikeACheck(kind, value))
                {
                    findings.Add(new ShapeFinding(LineOf(text, match.Index), kind));
                }
            }
        }

        foreach (Match match in SecretAssignment.Matches(text))
        {
            var value = match.Groups["v"].Value;
            if (!StandIn.IsMatch(value) && (strict || LooksRandom(value)))
            {
                findings.Add(new ShapeFinding(LineOf(text, match.Index), "secret assignment"));
            }
        }

        return findings.Distinct().ToList();
    }

    /// <summary>
    /// The things a long-token pattern meets that are not keys: one character repeated (a fixture
    /// fingerprint), an EF migration id, the base64 of a PNG, and a PascalCase name — a test method,
    /// a migration class — where capitals and digits are a small share of the letters. A random
    /// base62 key is roughly two-thirds capitals and digits.
    /// </summary>
    private static bool LooksLikeACheck(string kind, string value)
    {
        if (value.Distinct().Count() == 1)
        {
            return true;
        }

        if (kind is "long token" or "quoted long token")
        {
            return MigrationId.IsMatch(value)
                || value.StartsWith("iVBORw0KGgo", StringComparison.Ordinal)
                || !LooksRandom(value);
        }

        return false;
    }

    /// <summary>12+ characters, no spaces, a letter and a digit, capitals plus digits at least 30% of it, and not mostly words.</summary>
    internal static bool LooksRandom(string value)
    {
        if (value.Length < 12 || value.Any(char.IsWhiteSpace)
            || !value.Any(char.IsDigit) || !value.Any(char.IsLetter))
        {
            return false;
        }

        var dense = value.Count(c => char.IsUpper(c) || char.IsDigit(c));
        return dense * 10 >= value.Length * 3 && !MostlyWords(value);
    }

    /// <summary>
    /// Whether half or more of <paramref name="value"/> sits in lowercase runs of two or more: the
    /// shape of words joined in PascalCase. In a random base62 key such runs are rare.
    /// </summary>
    private static bool MostlyWords(string value)
    {
        var inRuns = 0;
        var run = 0;
        foreach (var c in value + " ")
        {
            if (char.IsLower(c))
            {
                run++;
                continue;
            }

            if (run >= 2)
            {
                inRuns += run;
            }

            run = 0;
        }

        return inRuns * 2 >= value.Length;
    }

    internal static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>
/// Shapes of real contact and household data, found by line: email addresses outside the reserved
/// domains, phone numbers outside the fictional ranges, street addresses, real lender names, and
/// people outside the invented household (Sam, Lee, Robin, Juniper) where a value can only be a person.
/// A detector cannot know a real value; it catches the shapes a real value takes, and review does the rest.
/// </summary>
public static class InventedShapes
{
    public const string EmailKind = "email address outside the reserved domains";
    public const string PhoneKind = "phone number outside the fictional ranges";
    public const string StreetKind = "street address";
    public const string LenderKind = "real lender name";
    public const string PersonKind = "person outside the invented household";

    /// <summary>The invented household, compared without case.</summary>
    private static readonly string[] Household = ["Sam", "Lee", "Robin", "Juniper"];

    /// <summary>
    /// RFC 2606 / 6761 names, and two Google hosts that are identifiers rather than mailboxes: a Gmail
    /// Message-Id (<c>…@mail.gmail.com</c>) and a calendar id (<c>…@group.calendar.google.com</c>).
    /// </summary>
    private static readonly string[] ReservedDomains = ["example.com", "example.org", "example.net"];

    private static readonly string[] ReservedTlds = ["example", "test", "invalid", "localhost"];

    private static readonly string[] IdentifierHosts =
        ["mail.gmail.com", "group.calendar.google.com", "group.v.calendar.google.com", "users.noreply.github.com"];

    /// <summary>The commit trailer's address, exactly; anyone else at that domain is reported.</summary>
    private const string CommitTrailerAddress = "noreply@anthropic.com";

    /// <summary>
    /// local@domain, where the domain has a dot and ends in letters. The last rule is what leaves
    /// <c>pkg@1.2.3</c>, <c>actions/checkout@v4</c>, <c>@string/app_name</c> and <c>user@localhost</c> alone.
    /// A match straight after <c>://</c> is a URL's user part, not a mailbox.
    /// </summary>
    private static readonly Regex Email = new(
        @"(?<![A-Za-z0-9._%+\-])(?<!://)(?<local>[A-Za-z0-9._%+\-]+)@(?<domain>[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,})");

    /// <summary>
    /// A North American number with separators: optional <c>+1</c>, optional area code, three digits,
    /// four digits. It may not start in the middle of a longer number, version or date.
    /// </summary>
    private static readonly Regex NorthAmericanPhone = new(
        @"(?<![0-9A-Za-z.\-+/#])(?:(?:\+?1[ .\-])?(?:\((?<area>\d{3})\)[ .\-]?|(?<area>\d{3})[ .\-]))?(?<exchange>\d{3})[ .\-](?<line>\d{4})(?!\d)");

    /// <summary>A bare E.164 number: a plus sign and the country code and digits run together.</summary>
    private static readonly Regex E164Phone = new(@"(?<![0-9A-Za-z.\-+/#])\+(?<digits>\d{10,15})(?!\d)");

    /// <summary>An international number: a plus sign, a country code, then groups of digits split by spaces, dots or hyphens.</summary>
    private static readonly Regex InternationalPhone = new(
        @"(?<![0-9A-Za-z.\-+/#])\+(?<country>\d{1,3})(?<rest>(?:[ .\-]\d{1,5}){2,5})(?!\d)");

    /// <summary>
    /// A house number, one to three capitalised words, then a street suffix. The first word may not be
    /// the invented <c>Example</c> or a product name that happens to end the same way (<c>Google Drive</c>).
    /// </summary>
    private static readonly Regex StreetAddress = new(
        @"(?<![0-9.,:/#$\-])\b\d{1,5}\s+(?!(?:Example|Google|Shared|My|Team)\b)(?:[A-Z][a-z'\-]+\s+){1,3}?"
        + @"(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Court|Ct|Way|Place|Pl|Terrace|Highway|Hwy)\b");

    /// <summary>
    /// The large US banks, card issuers and loan servicers, spelled as their brands are. A fixture
    /// that names one beside the household's figures is a lender list (CLAUDE.md), so fixtures use
    /// invented institutions (Redwood Bank, Cedar Hollow, Bluebird Card). Words that are ordinary
    /// English on their own (Discover, Ally, Regions, Citizens) are listed only with their second word.
    /// Case-sensitive, so a package id (<c>com.pnc.…</c>) is left alone.
    /// </summary>
    private static readonly Regex Lender = new(
        @"\b(?:Chase Bank|JPMorgan|Wells Fargo|Citibank|Citigroup|Citi Card|Capital One|Amex|American Express|PNC|Navient|Sallie Mae|Nelnet|MOHELA|Synchrony|Barclaycard|Ally Bank|Ally Financial|U\.S\. Bank|US Bank|Bank of America|BofA|Truist|USAA|SoFi|LendingClub|Navy Federal|Goldman Sachs|Apple Card|Rocket Mortgage|Mr\. Cooper|Aidvantage|Fifth Third|KeyBank|TD Bank|HSBC|Santander|Discover Card|Discover Bank|Discover it|Citizens Bank|Regions Bank|Toyota Financial|Ford Credit|GM Financial|Huntington Bank)\b");

    /// <summary>A literal assigned to a person-named member (C# or JSON), case-insensitive.</summary>
    private static readonly Regex PersonMember = new(
        @"(?i)(?<![A-Za-z0-9_])(?:Cook|Child|Children|Partner|Spouse|Parent|Kid|Person|Member|Owner|Guest|Eater|Diner|FirstName|GivenName|LastName|Surname|FamilyName)"
        + @"[""']?\s*(?:=>|=|:)\s*""(?<v>[^""\r\n]*)""");

    /// <summary>
    /// A plain <c>Name</c>, read only in a <c>Household*</c> file where it can only be a person. Not in
    /// an XML doc comment's <c>param name="…"</c>, and not in Index (<c>Ledger/</c>), whose household
    /// file names banks and accounts — the lender rule covers those.
    /// </summary>
    private static readonly Regex PlainName = new(
        @"(?i)(?<![A-Za-z0-9_])(?<!param )(?<!paramref )(?<!typeparam )(?<!typeparamref )Name[""']?\s*(?:=>|=|:)\s*""(?<v>[^""\r\n]*)""");

    /// <summary>A value shaped like a name: one or two capitalised words.</summary>
    private static readonly Regex PersonLike = new(@"^[A-Z][a-z'\-]+(?: [A-Z][a-z'\-]+)?$");

    private static readonly Regex Roster = new(@"(?i)\bhousehold\s*\((?<list>[^)]*)\)");

    private static readonly Regex RosterSeparator = new(@",|;|&|\band\b");

    private static readonly Regex CapitalisedWord = new(@"^[A-Z][a-z]+$");

    /// <summary>This file, which has to spell the lenders it looks for.</summary>
    public const string DetectorSource = "Tools/LogLoopReader/RepoShapes.cs";

    public static IReadOnlyList<ShapeFinding> Find(string relativePath, string text)
    {
        var findings = new List<ShapeFinding>();
        FindEmails(text, findings);
        FindPhones(text, findings);
        Add(StreetAddress, StreetKind, text, findings);
        if (relativePath != DetectorSource)
        {
            Add(Lender, LenderKind, text, findings);
        }
        FindPeopleInFixtures(relativePath, text, findings);
        if (relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            FindRosters(text, findings);
        }

        return findings.Distinct().ToList();
    }

    /// <summary>Whether <paramref name="domain"/> is reserved for documentation or is an identifier host.</summary>
    public static bool ReservedEmail(string local, string domain)
    {
        if (string.Equals(local + "@" + domain, CommitTrailerAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool Under(string parent) =>
            domain.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith("." + parent, StringComparison.OrdinalIgnoreCase);

        return ReservedDomains.Any(Under)
            || IdentifierHosts.Any(Under)
            || ReservedTlds.Any(tld => domain.EndsWith("." + tld, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The fictional ranges: 555-01xx (any area code), the unassigned area code 555, and Ofcom's
    /// drama range for UK mobiles (07700 900000–900999).
    /// </summary>
    public static bool FictionalNorthAmerican(string area, string exchange, string line) =>
        area == "555" || (exchange == "555" && line.StartsWith("01", StringComparison.Ordinal));

    private static void Add(Regex pattern, string kind, string text, List<ShapeFinding> findings)
    {
        foreach (Match match in pattern.Matches(text))
        {
            findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), kind));
        }
    }

    private static void FindEmails(string text, List<ShapeFinding> findings)
    {
        foreach (Match match in Email.Matches(text))
        {
            if (!ReservedEmail(match.Groups["local"].Value, match.Groups["domain"].Value))
            {
                findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), EmailKind));
            }
        }
    }

    private static void FindPhones(string text, List<ShapeFinding> findings)
    {
        foreach (Match match in NorthAmericanPhone.Matches(text))
        {
            if (!FictionalNorthAmerican(match.Groups["area"].Value, match.Groups["exchange"].Value, match.Groups["line"].Value))
            {
                findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), PhoneKind));
            }
        }

        foreach (Match match in E164Phone.Matches(text))
        {
            var digits = match.Groups["digits"].Value;
            var fictional = digits.Length == 11 && digits.StartsWith('1')
                && FictionalNorthAmerican(digits.Substring(1, 3), digits.Substring(4, 3), digits.Substring(7, 4));
            var drama = digits.StartsWith("447700900", StringComparison.Ordinal) && digits.Length == 12;
            if (!fictional && !drama)
            {
                findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), PhoneKind));
            }
        }

        foreach (Match match in InternationalPhone.Matches(text))
        {
            var country = match.Groups["country"].Value;
            var national = new string(match.Groups["rest"].Value.Where(char.IsDigit).ToArray());

            // +1 numbers are judged by the North American rule above.
            var drama = country == "44" && national.StartsWith("7700900", StringComparison.Ordinal);
            if (country != "1" && !drama)
            {
                findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), PhoneKind));
            }
        }
    }

    /// <summary>A fixture is a <c>Household*.cs|json</c> file or anything under a <c>Fixtures</c> folder.</summary>
    private static bool IsFixture(string relativePath, out bool isHousehold)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        var file = parts[^1];
        isHousehold = file.StartsWith("Household", StringComparison.OrdinalIgnoreCase)
            && (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            && !relativePath.StartsWith("Ledger/", StringComparison.Ordinal);
        var underFixtures = parts.Take(parts.Length - 1).Any(part => string.Equals(part, "Fixtures", StringComparison.OrdinalIgnoreCase));
        return isHousehold || underFixtures;
    }

    private static void FindPeopleInFixtures(string relativePath, string text, List<ShapeFinding> findings)
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
                if (PersonLike.IsMatch(value) && !IsHouseholdMember(value))
                {
                    findings.Add(new ShapeFinding(KeyShapes.LineOf(text, match.Index), PersonKind));
                }
            }
        }
    }

    /// <summary>
    /// <c>household (A, B and C)</c> is a roster when it holds two or more single capitalised words;
    /// <c>household (Foreword)</c> and <c>household (two adults)</c> are not. A roster naming anyone
    /// outside the invented four is reported at the outsider's line.
    /// </summary>
    private static void FindRosters(string text, List<ShapeFinding> findings)
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
                findings.Add(new ShapeFinding(KeyShapes.LineOf(text, at), PersonKind));
            }
        }
    }

    private static bool IsHouseholdMember(string value) =>
        Household.Contains(value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A latitude and longitude written as a pair, precise enough to find a house: four or more decimal
/// places on each, inside the valid ranges. A city's centre to two places is not reported.
/// </summary>
public static partial class CoordinatePairs
{
    public static bool Contains(string text)
    {
        foreach (Match match in Pair().Matches(text))
        {
            var latitude = double.Parse(match.Groups["lat"].Value, CultureInfo.InvariantCulture);
            var longitude = double.Parse(match.Groups["lon"].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?<![\d.])(?<lat>-?\d{1,2}\.\d{4,})\s*[,;/ ]\s*(?<lon>-?\d{1,3}\.\d{4,})(?![\d.])")]
    private static partial Regex Pair();
}
