using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// Review S8: the Body and vault trails ("body read weight: ok, 2 readings") can never be read off the device unless the
/// export carries them, and the export may carry no value. So a trail line goes through a filter that keeps only words
/// the app itself writes, counts beside the words that count things, and a time stamp; whatever else is on the line,
/// the whole line is withheld rather than trimmed (a trimmed line is a line someone has to trust).
///
/// <para>Every value in this file is invented. Planted private things are assembled from parts so no source line is a
/// real-looking fact.</para>
/// </summary>
public class TrailLinesTests
{
    private const string Stamp = "[Oct 18, 9:05 am] ";

    private static string Body(string message) => Stamp + "INFO Body — " + message;

    private static string Vault(string message) => Stamp + "INFO Vault — " + message;

    public static IEnumerable<object[]> TheRealTrail()
    {
        yield return [Body("body read weight: ok, 2 readings")];
        yield return [Body("body read steps: ok, 1 reading")];
        yield return [Body("body read: 6 not granted, 1 read, 0 not read, 12 readings stored")];
        yield return [Body("body read resting-heart-rate: denied")];
        yield return [Body("body read sleep: bed only, 3 readings")];
        yield return [Body("body install day recorded")];
        yield return [Body("body sync on open: partial, gap: background read not granted")];
        yield return [Body("body sync worker skipped: background read not granted")];
        yield return [Body("body sync worker failed: IOException")];
        yield return [Body("body bed sleep: failed (IOException)")];
        yield return [Body("body bed sleep: signinrejected")];
        yield return [Body("body bed sleep: login refused earlier, not asked")];
        yield return [Body("sleepiq sign-in: 200")];
        yield return [Body("sleepiq sleepers: 401")];
        yield return [Body("sleepiq sleep-data: offline (HttpRequestException)")];
        yield return [Body("sleepiq sleepers: unexpected shape (no sleepers list)")];
        yield return [Body("sleepiq side: none on the left")];
        yield return [Body("health permission steps: granted")];
        yield return [Body("health read steps: 12 records, 11 usable")];
        yield return [Body("health read sleep: denied (SecurityException)")];
        yield return [Body("health choices saved; vault publish published")];
        yield return [Body("body backup skipped: not signed in")];
        yield return [Body("body restore: skipped (history already here)")];
        yield return [Vault("vault restore: restored 3 fields (shared loaded, personal loaded)")];
        yield return [Vault("vault publish: shared published, personal nothing to publish")];
        yield return [Vault("vault restore skipped: no passphrase")];
        yield return [Vault("vault publish failed: HttpRequestException")];
    }

    [Theory]
    [MemberData(nameof(TheRealTrail))]
    public void TheRealTrailPassesUnchanged(string line)
    {
        Assert.Equal(line, TrailLines.Filter(line));
    }

    public static IEnumerable<object[]> PlantedPrivateThings()
    {
        yield return ["a weight with a unit", Body("body read weight: 18" + "7 lb")];
        yield return ["a decimal weight", Body("body read weight: 187" + ".4")];
        yield return ["a bare number beside a word", Body("body read weight: 18" + "7")];
        yield return ["a number after a colon", Body("body read steps: 84" + "12")];
        yield return ["a name", Body("body read: Zeph" + "yrine slept 7")];
        yield return ["a lowercase name", Body("body read for zeph" + "yrine: ok, 2 readings")];
        yield return ["an email", Body("sleepiq sign-in: sleeper" + "@example" + ".com")];
        yield return ["a long digit run", Body("body read steps: 12345" + "6789 readings")];
        yield return ["a date", Body("body install day recorded 2026" + "-11-17")];
        yield return ["a ratio", Body("pressure 120" + "/80 readings")];
        yield return ["a number glued to a unit", Body("body read weight: 81" + "kg")];
        yield return ["an underscore token", Body("body_read weight")];
        yield return ["an uppercase word", Body("Body read Weight: ok")];
        yield return ["a url", Body("see https:" + "//example.com")];
        yield return ["a quote", Body("body read \"weight\": ok")];
        yield return ["no prefix at all", "body read weight: ok, 2 readings"];
        yield return ["a source it does not know", Stamp + "INFO Elsewhere — body read weight: ok"];
        yield return ["a stamp it does not know", "[yesterday at lunch] INFO Body — body read weight: ok"];
        yield return ["an empty message", Body(string.Empty)];
    }

    [Theory]
    [MemberData(nameof(PlantedPrivateThings))]
    public void APlantedPrivateThingWithholdsTheWholeLine(string kind, string line)
    {
        Assert.True(TrailLines.Filter(line) is null, $"A line with {kind} was kept.");
    }

    [Fact]
    public void ALineTooLongIsWithheld()
    {
        var long160 = Body(string.Join(' ', Enumerable.Repeat("body", 60)));

        Assert.Null(TrailLines.Filter(long160));
    }

    [Fact]
    public void ACountIsByShapeACount()
    {
        // The honest limit: "187 readings" cannot be told from a count of readings. The trails never write a value,
        // so this is the second line of defence, and it is stated rather than hidden.
        Assert.NotNull(TrailLines.Filter(Body("body read weight: ok, 187 readings")));
    }

    [Fact]
    public void AFilteredLineNeverHoldsADigitOutsideACount()
    {
        foreach (var line in TheRealTrail().Select(row => (string)row[0]).Select(TrailLines.Filter))
        {
            var message = line![(line.IndexOf('—', StringComparison.Ordinal) + 1)..];
            foreach (var token in message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => token.Any(char.IsDigit)))
            {
                Assert.Matches(@"^\(?[0-9]{1,4}[,:)]*$", token);
            }
        }
    }

    // ---- the last forty ----

    [Fact]
    public void TheLastLinesAreTheNewestOnesInOrder()
    {
        var trail = string.Concat(Enumerable.Range(0, 60).Select(n => Body($"body read steps: ok, {n} readings") + "\n\n"));

        var lines = TrailLines.Last(trail);

        Assert.Equal(TrailLines.MostRecent, lines.Count);
        Assert.Equal(TrailLines.MostRecent, 40);
        Assert.Equal(Body("body read steps: ok, 20 readings"), lines[0].Value);
        Assert.Equal(Body("body read steps: ok, 59 readings"), lines[^1].Value);
    }

    [Fact]
    public void AWithheldLineIsMarkedAndNotDropped()
    {
        var trail = Body("body read steps: ok, 2 readings") + "\n\n"
            + Body("body read weight: 18" + "7 lb") + "\n\n"
            + Body("body read sleep: ok, 1 reading") + "\n\n";

        var lines = TrailLines.Last(trail).Select(line => line.Value).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal(TrailLines.Withheld, lines[1]);
        Assert.NotNull(TrailLines.Filter(Body(TrailLines.Withheld)));
        Assert.DoesNotContain(lines, line => line.Contains("187", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExceptionsDetailLinesAreNotPartOfTheTrail()
    {
        var trail = Body("body sync worker failed: IOException") + "\n   at Some.Stack.Frame() in file.cs:line 12\n\n";

        var line = Assert.Single(TrailLines.Last(trail));

        Assert.Equal(Body("body sync worker failed: IOException"), line.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n\n")]
    public void NothingRecordedIsNoLines(string trail)
    {
        Assert.Empty(TrailLines.Last(trail));
    }

    [Fact]
    public void AnythingBuiltFromRawTextGoesThroughTheFilter()
    {
        Assert.Null(TrailLine.From(Body("body read weight: 18" + "7 lb")));
        Assert.Equal(Body("body read weight: ok, 2 readings"), TrailLine.From(Body("body read weight: ok, 2 readings"))!.Value);
    }
}
