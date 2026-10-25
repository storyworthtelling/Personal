using System.Text.Json;
using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.2.1 · Skips and done days. At the end of a day, a session whose pages carry no mark is a skip: the plan eases
/// by the existing frequency rules, nothing is said (D18 makes silence an answer, D17/NotRead keep an unread page
/// from being one), and the done and skipped days feed the digest.
/// </summary>
public class SkipLedgerTests
{
    private static readonly string[] Boxes = ["workout-box", "done-1", "done-2"];

    private static SessionMarks Marks(int day, SessionKind kind = SessionKind.Strength, string person = Sam, string[]? ids = null) =>
        new(person, Day(day), kind, ids ?? Boxes);

    private static InkResolution[] AllEmpty() => Boxes.Select(id => (InkResolution)new InkResolution.Skipped(id)).ToArray();

    // ---- UntickedIsSkipped ----

    [Fact]
    public void UntickedIsSkipped()
    {
        Assert.Equal(SessionOutcome.Skipped, DayClose.Decide(Marks(0), AllEmpty()));

        var ledger = SkipLedger.Empty.Settle(Marks(0), AllEmpty());

        Assert.Equal(SessionOutcome.Skipped, ledger.OutcomeOf(Sam, Day(0)));
        Assert.Equal([new SessionRecord(Sam, Day(0), SessionKind.Strength, SessionOutcome.Skipped)], ledger.Records.ToArray());
    }

    public static TheoryData<InkResolution> AnyMark => new()
    {
        new InkResolution.Ticked("done-2"),
        new InkResolution.PageDone("done-1", "right"),
        new InkResolution.Written("workout-box", "12"),
        new InkResolution.Circled("workout-box", "Toes"),
    };

    [Theory]
    [MemberData(nameof(AnyMark))]
    public void AnyMarkOnAnyPageIsDone(InkResolution mark)
    {
        var resolutions = AllEmpty().Where(item => item.SubjectId != mark.SubjectId).Append(mark).ToArray();

        Assert.Equal(SessionOutcome.Done, DayClose.Decide(Marks(0), resolutions));
    }

    [Fact]
    public void AnUnreadPageIsNotASkip()
    {
        InkResolution[] unread = [new InkResolution.NotRead("workout-box", "page not read"), new InkResolution.Skipped("done-1"), new InkResolution.Skipped("done-2")];

        Assert.Equal(SessionOutcome.Unsettled, DayClose.Decide(Marks(0), unread));
        Assert.Empty(SkipLedger.Empty.Settle(Marks(0), unread).Records);
    }

    [Fact]
    public void AQuestionOnThePageWaitsForItsAnswer()
    {
        InkResolution[] asked =
        [
            new InkResolution.NeedsAsking("done-1", "push-ups", "Ticked", "Not ticked", "cause"),
            new InkResolution.Skipped("workout-box"),
            new InkResolution.Skipped("done-2"),
        ];

        Assert.Equal(SessionOutcome.Unsettled, DayClose.Decide(Marks(0), asked));
    }

    [Fact]
    public void AMarkBeatsAnUnreadPage()
    {
        InkResolution[] mixed = [new InkResolution.NotRead("done-1", "page not read"), new InkResolution.Ticked("done-2"), new InkResolution.Skipped("workout-box")];

        Assert.Equal(SessionOutcome.Done, DayClose.Decide(Marks(0), mixed));
    }

    [Fact]
    public void NoReadAtAllIsNotASkip()
    {
        Assert.Equal(SessionOutcome.Unsettled, DayClose.Decide(Marks(0), []));
    }

    [Fact]
    public void OnlyThisSessionsBoxesCount()
    {
        InkResolution[] elsewhere = [new InkResolution.Ticked("someone-elses-box"), .. AllEmpty()];
        InkResolution[] onlyElsewhere = [new InkResolution.Skipped("someone-elses-box")];

        Assert.Equal(SessionOutcome.Skipped, DayClose.Decide(Marks(0), elsewhere));
        Assert.Equal(SessionOutcome.Unsettled, DayClose.Decide(Marks(0), onlyElsewhere));
    }

    [Fact]
    public void ARatingAloneNeverMakesAnotherBoxMeanDone()
    {
        InkResolution[] unchanged = [new InkResolution.Unchanged("rating-1"), .. AllEmpty()];

        Assert.Equal(SessionOutcome.Skipped, DayClose.Decide(Marks(0, ids: [.. Boxes, "rating-1"]), unchanged));
    }

    [Fact]
    public void ASessionWithNoBoxesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DayClose.Decide(new SessionMarks(Sam, Day(0), SessionKind.Strength, []), AllEmpty()));
    }

    // ---- the ledger ----

    [Fact]
    public void ALaterUnsettledNeverErasesAnAnswer()
    {
        var done = SkipLedger.Empty.Settle(Marks(0), [new InkResolution.Ticked("done-1")]);

        var after = done.Settle(Marks(0), [new InkResolution.NotRead("done-1", "page not read")]);

        Assert.Equal(SessionOutcome.Done, after.OutcomeOf(Sam, Day(0)));
    }

    [Fact]
    public void ASettledReadReplacesAnEarlierOne()
    {
        var skipped = SkipLedger.Empty.Settle(Marks(0), AllEmpty());

        var later = skipped.Settle(Marks(0), [new InkResolution.Ticked("done-1")]);

        Assert.Equal(SessionOutcome.Done, later.OutcomeOf(Sam, Day(0)));
        Assert.Single(later.Records);
        Assert.Equal(SessionOutcome.Skipped, skipped.OutcomeOf(Sam, Day(0)));
    }

    [Fact]
    public void ANameIsMatchedWithoutCase()
    {
        var ledger = SkipLedger.Empty.Settle(Marks(0, person: "sam"), AllEmpty());

        Assert.Equal(SessionOutcome.Skipped, ledger.OutcomeOf("SAM", Day(0)));
        Assert.Null(ledger.OutcomeOf(Lee, Day(0)));
        Assert.Null(ledger.OutcomeOf(Sam, Day(1)));
    }

    [Fact]
    public void TheLastDoneDayIsTheLatestDoneOne()
    {
        var ledger = Week(done: [0, 2], skipped: [4]);

        Assert.Equal(Day(2), ledger.LastDoneDay(Sam));
        Assert.Null(ledger.LastDoneDay(Lee));
        Assert.Null(SkipLedger.Empty.LastDoneDay(Sam));
    }

    // ---- ASkipSaysNothing ----

    [Fact]
    public void ASkipSaysNothing()
    {
        var ledger = SkipLedger.Empty.Settle(Marks(0), AllEmpty());

        // The only thing a skip produces is a record: no notice, no voiced line, no text.
        Assert.IsType<SkipLedger>(ledger);
        Type[] types = [typeof(SkipLedger), typeof(SessionRecord), typeof(SessionMarks), typeof(WeekRecord), typeof(DayClose), typeof(SkipLedgerRead)];
        foreach (var type in types)
        {
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (var word in new[] { "Message", "Notice", "Voice", "Say", "Line", "Reason", "Toast", "Text", "Tell" })
                {
                    Assert.DoesNotContain(word, member.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var reached = types
            .SelectMany(type => type.GetMembers())
            .SelectMany(member => member switch
            {
                System.Reflection.MethodInfo method => method.GetParameters().Select(item => item.ParameterType).Append(method.ReturnType),
                System.Reflection.PropertyInfo property => [property.PropertyType],
                System.Reflection.FieldInfo field => [field.FieldType],
                _ => [],
            })
            .Select(type => type.Namespace ?? string.Empty);
        Assert.DoesNotContain(reached, space => space.StartsWith("Constitution.Domain.Notices", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, space => space.StartsWith("Constitution.Domain.Voice", StringComparison.Ordinal));
        Assert.DoesNotContain(reached, space => space.StartsWith("Constitution.Domain.Waiting", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStoredFileHoldsNoWords()
    {
        var json = SkipLedger.Empty.Settle(Marks(0), AllEmpty()).ToJson();

        using var document = JsonDocument.Parse(json);
        var keys = new HashSet<string>();
        Collect(document.RootElement, keys);
        Assert.Equal(["day", "kind", "outcome", "person", "records", "version"], keys.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    // ---- TheDigestSeesTheWeek ----

    [Fact]
    public void TheDigestSeesTheWeek()
    {
        var ledger = Week(done: [0, 4, 6], skipped: [2])
            .Settle(Marks(-3), AllEmpty())
            .Settle(Marks(7), [new InkResolution.Ticked("done-1")])
            .Settle(Marks(0, person: Lee), [new InkResolution.Ticked("done-1")]);

        var week = ledger.Week(Sam, WeekStart);

        Assert.Equal(Sam, week.Person);
        Assert.Equal(WeekStart, week.WeekStart);
        Assert.Equal([Day(0), Day(4), Day(6)], week.DoneDays.ToArray());
        Assert.Equal([Day(2)], week.SkippedDays.ToArray());
        Assert.Equal([Day(0)], ledger.Week(Lee, WeekStart).DoneDays.ToArray());
        Assert.Empty(ledger.Week(Lee, WeekStart).SkippedDays);
    }

    [Fact]
    public void AWeekWithNothingIsEmptyNotMissing()
    {
        var week = SkipLedger.Empty.Week(Sam, WeekStart);

        Assert.Empty(week.DoneDays);
        Assert.Empty(week.SkippedDays);
    }

    // ---- the plan eases ----

    [Fact]
    public void SkipsReduceFrequencyByTheExistingRules()
    {
        // Last week is the week before WeekStart: days -7 to -1.
        var twoSkips = Last(done: [-6], skipped: [-4, -2]);
        var oneSkip = Last(done: [-6, -4], skipped: [-2]);
        var fullWeek = Last(done: [-6, -4, -2], skipped: []);

        Assert.Equal(2, WorkoutFrequency.Target(twoSkips.RecoveryFor(Sam, WeekStart)));
        Assert.Equal(3, WorkoutFrequency.Target(oneSkip.RecoveryFor(Sam, WeekStart)));
        Assert.Equal(4, WorkoutFrequency.Target(fullWeek.RecoveryFor(Sam, WeekStart)));
    }

    [Fact]
    public void TheCountsAreLastWeeksOnly()
    {
        var ledger = Last(done: [-6], skipped: [-4, -2]).Settle(Marks(-9, SessionKind.Strength), AllEmpty()).Settle(Marks(2), AllEmpty());

        var recovery = ledger.RecoveryFor(Sam, WeekStart);

        Assert.Equal(1, recovery.SessionsDoneLastWeek);
        Assert.Equal(2, recovery.SessionsSkippedLastWeek);
    }

    [Fact]
    public void LastWeekRunsFromTheMondayBackToTheSunday()
    {
        // Day -7 is last week's first day and day -1 its last; day -8 and day 0 are neither.
        var ledger = SkipLedger.Empty
            .Settle(Marks(-8), AllEmpty())
            .Settle(Marks(-7), AllEmpty())
            .Settle(Marks(-1, SessionKind.Mobility), [new InkResolution.Ticked("done-1")])
            .Settle(Marks(0), AllEmpty());

        var recovery = ledger.RecoveryFor(Sam, WeekStart);

        Assert.Equal(1, recovery.SessionsDoneLastWeek);
        Assert.Equal(1, recovery.SessionsSkippedLastWeek);
    }

    [Fact]
    public void NoSettledSessionLastWeekIsNoHistory()
    {
        var recovery = Week(done: [0], skipped: []).RecoveryFor(Sam, WeekStart);

        Assert.Null(recovery.SessionsDoneLastWeek);
        Assert.Equal(0, recovery.SessionsSkippedLastWeek);
        Assert.Equal(3, WorkoutFrequency.Target(recovery));
    }

    [Fact]
    public void AGentleWalkIsNotCountedEitherWay()
    {
        var ledger = Last(done: [-6, -4], skipped: [])
            .Settle(Marks(-1, SessionKind.GentleWalk), AllEmpty())
            .Settle(Marks(-8, SessionKind.GentleWalk), [new InkResolution.Ticked("done-1")]);

        var recovery = ledger.RecoveryFor(Sam, WeekStart);

        Assert.Equal(2, recovery.SessionsDoneLastWeek);
        Assert.Equal(0, recovery.SessionsSkippedLastWeek);
        // ...but the digest still sees the walks.
        Assert.Contains(Day(-1), ledger.Week(Sam, WeekStart.AddDays(-7)).SkippedDays);
    }

    [Fact]
    public void TheLastHardDayIsTheLatestHardOneDone()
    {
        var ledger = SkipLedger.Empty
            .Settle(Marks(-6, SessionKind.Strength), [new InkResolution.Ticked("done-1")])
            .Settle(Marks(-3, SessionKind.OutdoorCardio), [new InkResolution.Ticked("done-1")])
            .Settle(Marks(-2, SessionKind.Mobility), [new InkResolution.Ticked("done-1")])
            .Settle(Marks(-1, SessionKind.Strength), AllEmpty())
            .Settle(Marks(1, SessionKind.Strength), [new InkResolution.Ticked("done-1")]);

        Assert.Equal(Day(-3), ledger.RecoveryFor(Sam, WeekStart).LastHardSessionDay);
        Assert.Null(SkipLedger.Empty.RecoveryFor(Sam, WeekStart).LastHardSessionDay);
    }

    [Fact]
    public void ASkippedHardSessionAsksForNoRecovery()
    {
        var ledger = SkipLedger.Empty.Settle(Marks(-1, SessionKind.Strength), AllEmpty());

        Assert.Null(ledger.RecoveryFor(Sam, WeekStart).LastHardSessionDay);
    }

    [Fact]
    public void TwoSkipsMeanAnEasierPlan()
    {
        var fresh = SamOf(Plan(Calendars(), bothAdults: false)).Sessions.Where(item => item.Day.DayOfWeek != DayOfWeek.Sunday).ToList();
        var recovery = Last(done: [-6], skipped: [-4, -2]).RecoveryFor(Sam, WeekStart);

        var eased = SamOf(Plan(Calendars(), bothAdults: false, sam: recovery)).Sessions.Where(item => item.Day.DayOfWeek != DayOfWeek.Sunday).ToList();

        Assert.Equal(3, fresh.Count);
        Assert.Equal(2, eased.Count);
    }

    // ---- the stored file ----

    [Fact]
    public void TheFileIsPinnedLiterally()
    {
        var ledger = SkipLedger.Empty.Settle(Marks(0), AllEmpty());

        Assert.Equal(
            "{\"version\":1,\"records\":[{\"person\":\"Sam\",\"day\":\"2026-11-02\",\"kind\":\"strength\",\"outcome\":\"skipped\"}]}",
            ledger.ToJson());
    }

    [Fact]
    public void EveryKindHasAStoredName()
    {
        var ledger = SkipLedger.Empty;
        var day = 0;
        foreach (var kind in Enum.GetValues<SessionKind>())
        {
            ledger = ledger.Settle(Marks(day++, kind), [new InkResolution.Ticked("done-1")]);
        }

        var read = SkipLedger.FromJson(ledger.ToJson());

        Assert.Equal(Enum.GetValues<SessionKind>().Length, read.Ledger.Records.Count);
        Assert.Equal(ledger.Records.ToArray(), read.Ledger.Records.ToArray());
        Assert.Equal(0, read.UnreadableEntries);
        Assert.False(read.WholeFileUnreadable);
        var names = ledger.ToJson();
        foreach (var literal in new[] { "strength", "outdoor-cardio", "treadmill", "mobility", "gentle-walk" })
        {
            Assert.Contains($"\"{literal}\"", names);
        }
    }

    [Fact]
    public void ADoneRecordRoundTrips()
    {
        var ledger = Week(done: [0, 2], skipped: [4]);

        var read = SkipLedger.FromJson(ledger.ToJson());

        Assert.Equal(ledger.Records.ToArray(), read.Ledger.Records.ToArray());
        Assert.Equal(3, read.Ledger.Records.Count);
        Assert.Equal(ledger.ToJson(), read.Ledger.ToJson());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingStoredIsAnEmptyLedgerWithNoProblem(string? stored)
    {
        var read = SkipLedger.FromJson(stored);

        Assert.Empty(read.Ledger.Records);
        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(0, read.UnreadableEntries);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":2,\"records\":[]}")]
    [InlineData("{\"records\":[]}")]
    [InlineData("{\"version\":1}")]
    public void AFileThatCannotBeReadIsSaidToBeUnreadable(string stored)
    {
        var read = SkipLedger.FromJson(stored);

        Assert.True(read.WholeFileUnreadable);
        Assert.Empty(read.Ledger.Records);
    }

    [Fact]
    public void OneBadEntryDoesNotLoseTheRest()
    {
        const string stored =
            "{\"version\":1,\"records\":["
            + "{\"person\":\"Sam\",\"day\":\"2026-11-02\",\"kind\":\"strength\",\"outcome\":\"done\"},"
            + "{\"person\":\"Sam\",\"day\":\"2026-11-03\",\"kind\":\"rowing\",\"outcome\":\"done\"},"
            + "{\"person\":\"Sam\",\"day\":\"soon\",\"kind\":\"mobility\",\"outcome\":\"done\"},"
            + "{\"person\":\"Sam\",\"day\":\"2026-11-04\",\"kind\":\"mobility\",\"outcome\":\"unsettled\"},"
            + "{\"person\":\"\",\"day\":\"2026-11-05\",\"kind\":\"mobility\",\"outcome\":\"done\"},"
            + "{\"person\":\"Lee\",\"day\":\"2026-11-06\",\"kind\":\"mobility\",\"outcome\":\"skipped\"}]}";

        var read = SkipLedger.FromJson(stored);

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(4, read.UnreadableEntries);
        Assert.Equal(
            [new SessionRecord(Sam, Day(0), SessionKind.Strength, SessionOutcome.Done), new SessionRecord(Lee, Day(4), SessionKind.Mobility, SessionOutcome.Skipped)],
            read.Ledger.Records.ToArray());
    }

    private const string Yoga = "{\"person\":\"Sam\",\"day\":\"2026-11-03\",\"kind\":\"yoga\",\"outcome\":\"done\"}";
    private const string Odd = "{ \"person\" : \"\", \"day\":\"soon\", \"extra\":[1,2] }";

    private static string WithUnreadable() =>
        "{\"version\":1,\"records\":["
        + "{\"person\":\"Sam\",\"day\":\"2026-11-02\",\"kind\":\"strength\",\"outcome\":\"done\"},"
        + Yoga + ","
        + Odd + ","
        + "{\"person\":\"Lee\",\"day\":\"2026-11-06\",\"kind\":\"mobility\",\"outcome\":\"skipped\"}]}";

    [Fact]
    public void UnreadableEntriesAreWrittenBackVerbatim()
    {
        var written = SkipLedger.FromJson(WithUnreadable()).Ledger.ToJson();

        Assert.Contains(Yoga, written, StringComparison.Ordinal);
        Assert.Contains(Odd, written, StringComparison.Ordinal);
        var again = SkipLedger.FromJson(written);
        Assert.Equal(2, again.UnreadableEntries);
        Assert.Equal(2, again.Ledger.Records.Count);
    }

    [Fact]
    public void ASaveThatAddsARecordKeepsThemToo()
    {
        var read = SkipLedger.FromJson(WithUnreadable());

        var next = read.Ledger.Settle(Marks(9), AllEmpty());

        Assert.Equal(3, next.Records.Count);
        var written = next.ToJson();
        Assert.Contains(Yoga, written, StringComparison.Ordinal);
        Assert.Contains(Odd, written, StringComparison.Ordinal);
        Assert.Equal(2, SkipLedger.FromJson(written).UnreadableEntries);
    }

    [Fact]
    public void WritingTwiceIsByteStable()
    {
        var first = SkipLedger.FromJson(WithUnreadable()).Ledger.Settle(Marks(9), AllEmpty()).ToJson();

        var second = SkipLedger.FromJson(first).Ledger.ToJson();

        Assert.Equal(first, second);
    }

    [Fact]
    public void ALedgerWithNothingUnreadableWritesNoExtras()
    {
        var read = SkipLedger.FromJson(SkipLedger.Empty.Settle(Marks(0), AllEmpty()).ToJson());

        Assert.Equal(0, read.UnreadableEntries);
        Assert.DoesNotContain("yoga", read.Ledger.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsComeBackInDayOrder()
    {
        var ledger = Week(done: [4, 0], skipped: [2]);

        Assert.Equal([Day(0), Day(2), Day(4)], ledger.Records.Select(item => item.Day).ToArray());
    }

    // ---- helpers ----

    private static SkipLedger Week(int[] done, int[] skipped) => Build(0, done, skipped);

    private static SkipLedger Last(int[] done, int[] skipped) => Build(0, done, skipped);

    private static SkipLedger Build(int start, int[] done, int[] skipped)
    {
        var ledger = SkipLedger.Empty;
        foreach (var day in done)
        {
            ledger = ledger.Settle(Marks(day + start, KindOn(day)), [new InkResolution.Ticked("done-1")]);
        }

        foreach (var day in skipped)
        {
            ledger = ledger.Settle(Marks(day + start, KindOn(day)), AllEmpty());
        }

        return ledger;
    }

    private static SessionKind KindOn(int day) => Day(day).DayOfWeek == DayOfWeek.Sunday ? SessionKind.GentleWalk : SessionKind.Strength;

    private static void Collect(JsonElement element, HashSet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    keys.Add(property.Name);
                    Collect(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, keys);
                }

                break;
        }
    }
}
