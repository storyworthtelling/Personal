using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Storyworthtelling.Household;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>
/// The stored shape of a meal answer. The file outlives the build, so every word here is typed out as a literal:
/// a renamed field or verdict word is a reader that finds nothing while the answers sit in Drive.
/// </summary>
public class MealAnswerMapTests
{
    private static readonly HlcTimestamp Stamp = new(1_000, 0);

    private static HouseholdRecord Stored(MealAnswer answer, string install = "install-sam") =>
        HouseholdRecord.Write(MealAnswerMaps.Answer.KeyOf(answer), RecordSchema.Version, MealAnswerMaps.Answer.FieldsOf(answer), Stamp, install);

    [Fact]
    public void TheKindIsTheFrozenWeekPlan()
    {
        Assert.Equal("week-plan", MealAnswerMaps.Answer.Kind);
        Assert.Equal(ConstitutionKinds.WeekPlan, MealAnswerMaps.Answer.Kind);
    }

    [Fact]
    public void TheKeyIsPersonWeekAndItem()
    {
        var key = MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60)));
        Assert.Equal("week-plan/answer:sam:2026-10-25:dinner-tue-yes", key.Value);
    }

    [Fact]
    public void TwoAdultsAreTwoKeys()
    {
        Assert.NotEqual(
            MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60))),
            MealAnswerMaps.Answer.KeyOf(Tick(Lee, Tuesday, Before(60))));
    }

    [Fact]
    public void AReadingTimeIsNotPartOfTheKey()
    {
        Assert.Equal(
            MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60))),
            MealAnswerMaps.Answer.KeyOf(Deny(Sam, Tuesday, "no", After(60))));
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        var fields = MealAnswerMaps.Answer.FieldsOf(Deny(Lee, Tuesday, "Robin has swim", Before(60)));
        Assert.Equal(["item", "person", "read", "reason", "verdict", "week"], fields.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("lee", fields["person"]);
        Assert.Equal("2026-10-25", fields["week"]);
        Assert.Equal("dinner-tue-yes", fields["item"]);
        Assert.Equal("Robin has swim", fields["reason"]);
    }

    [Fact]
    public void TheVerdictWordsAreFrozen()
    {
        Assert.Equal("approved", MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, Before(60)))["verdict"]);
        Assert.Equal("denied", MealAnswerMaps.Answer.FieldsOf(Deny(Sam, Tuesday, null, Before(60)))["verdict"]);
        Assert.Equal("unchanged", MealAnswerMaps.Answer.FieldsOf(Silent(Sam, Tuesday, Before(60)))["verdict"]);
    }

    [Fact]
    public void TheReadingTimeIsUnixMilliseconds()
    {
        var at = Before(60);
        Assert.Equal(at.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, at))["read"]);
    }

    [Fact]
    public void EveryKindOfAnswerRoundTrips()
    {
        foreach (var answer in new[]
        {
            Tick(Sam, Tuesday, Before(60)),
            Deny(Lee, Wednesday, "Robin's swim: back at 7:30 — pasta, please", After(10)),
            Deny(Lee, Thursday, null, Before(5)),
            Silent(Sam, Thursday, Before(75)),
        })
        {
            var read = MealAnswerMaps.Answer.Read(new RecordReader(Stored(answer)));
            Assert.Equal(answer.Person, read.Person);
            Assert.Equal(answer.Week, read.Week);
            Assert.Equal(answer.ItemId, read.ItemId);
            Assert.Equal(answer.Verdict, read.Verdict);
            Assert.Equal(answer.Reason, read.Reason);
            Assert.Equal(answer.ReadAt.ToUnixTimeMilliseconds(), read.ReadAt.ToUnixTimeMilliseconds());
        }
    }

    [Fact]
    public void AWordThisBuildDoesNotKnowIsUnreadable()
    {
        var fields = MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, Before(60))).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["verdict"] = "maybe";
        var record = HouseholdRecord.Write(MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60))), 1, fields, Stamp, "install-sam");
        var exception = Assert.Throws<HouseholdRecordException>(() => MealAnswerMaps.Answer.Read(new RecordReader(record)));
        Assert.Contains("verdict", exception.Message);
    }

    [Fact]
    public void ARecordFilledOutOfItsKeyIsUnreadable()
    {
        var wrongKey = MealAnswerMaps.Answer.KeyOf(Tick(Lee, Tuesday, Before(60)));
        var record = HouseholdRecord.Write(wrongKey, 1, MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, Before(60))), Stamp, "install-sam");
        Assert.Throws<HouseholdRecordException>(() => MealAnswerMaps.Answer.Read(new RecordReader(record)));
    }

    [Fact]
    public void ARecordWithoutAReadingTimeIsUnreadable()
    {
        var fields = MealAnswerMaps.Answer.FieldsOf(Tick(Sam, Tuesday, Before(60))).Where(pair => pair.Key != "read").ToDictionary(pair => pair.Key, pair => pair.Value);
        var record = HouseholdRecord.Write(MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60))), 1, fields, Stamp, "install-sam");
        var exception = Assert.Throws<HouseholdRecordException>(() => MealAnswerMaps.Answer.Read(new RecordReader(record)));
        Assert.Contains("read", exception.Message);
    }

    [Fact]
    public void OnlyAnAnswerKeyIsAnAnswerKey()
    {
        Assert.True(MealAnswerKeys.IsAnswer(MealAnswerMaps.Answer.KeyOf(Tick(Sam, Tuesday, Before(60)))));
        Assert.False(MealAnswerKeys.IsAnswer(new RecordKey(ConstitutionKinds.WeekPlan, "plan:2026-10-25")));
        Assert.False(MealAnswerKeys.IsAnswer(new RecordKey(ConstitutionKinds.Recipe, "answer:sam:2026-10-25:dinner-tue-yes")));
    }
}
