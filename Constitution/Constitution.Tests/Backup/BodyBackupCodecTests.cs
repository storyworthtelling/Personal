using System.Text.Json;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;

namespace Constitution.Tests.Backup;

/// <summary>
/// C-7.4.1 — the plaintext inside the Body backup's envelope. What matters here is that the shape is
/// frozen (the ids and property names are typed out as literals, because they are what a restore on a
/// new phone has to find), that a backup from a newer build is read as far as it can be and never
/// guessed at, and that a bad file is a typed error which never echoes what was in it.
/// </summary>
public class BodyBackupCodecTests
{
    private static BodyHistory Everything() =>
        BodyHistory.Create(BackupInvented.InstallDay, BackupInvented.EverySignal());

    // ---- the frozen shape ----

    [Fact]
    public void TheFormatIsVersionedAndGroupedBySignalId()
    {
        using var document = JsonDocument.Parse(BodyBackupCodec.Write(Everything()));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("2026-11-01", root.GetProperty("installDay").GetString());
        Assert.Equal(
            [
                "weight", "body-composition", "resting-heart-rate", "heart-rate-variability", "steps",
                "active-minutes", "sleep", "strength", "calories",
            ],
            root.GetProperty("readings").EnumerateObject().Select(group => group.Name).ToArray());
    }

    [Fact]
    public void EachReadingIsADayAndAValue()
    {
        using var document = JsonDocument.Parse(BodyBackupCodec.Write(
            BodyHistory.Create(null, [BackupInvented.One(BodySignal.Weight, new DateOnly(2026, 11, 3), 182.5)])));

        var reading = Assert.Single(document.RootElement.GetProperty("readings").GetProperty("weight").EnumerateArray());
        Assert.Equal("2026-11-03", reading.GetProperty("day").GetString());
        Assert.Equal(182.5, reading.GetProperty("value").GetDouble());
        Assert.Equal(2, reading.EnumerateObject().Count());
    }

    [Fact]
    public void ASignalWithNoReadingsHasNoGroup()
    {
        using var document = JsonDocument.Parse(BodyBackupCodec.Write(
            BodyHistory.Create(null, [BackupInvented.One(BodySignal.Steps, BackupInvented.FirstDay, 4000)])));

        Assert.Equal(["steps"], document.RootElement.GetProperty("readings").EnumerateObject().Select(group => group.Name));
    }

    [Fact]
    public void TheInstallDayIsOptional()
    {
        var written = BodyBackupCodec.Write(BodyHistory.Create(null, BackupInvented.EverySignal()));

        Assert.Null(BodyBackupCodec.Read(written).History.InstallDay);
    }

    [Fact]
    public void TheWriteIsTheSameWhateverOrderTheReadingsCameIn()
    {
        var readings = BackupInvented.EverySignal();
        var shuffled = readings.OrderByDescending(reading => reading.Value).ToList();

        Assert.Equal(
            BodyBackupCodec.Write(BodyHistory.Create(BackupInvented.InstallDay, readings)),
            BodyBackupCodec.Write(BodyHistory.Create(BackupInvented.InstallDay, shuffled)));
    }

    // ---- reading it back ----

    [Fact]
    public void EverySignalReadsBackWhatWasWritten()
    {
        var history = Everything();

        var decoded = BodyBackupCodec.Read(BodyBackupCodec.Write(history));

        Assert.Equal(history.Readings, decoded.History.Readings);
        Assert.Equal(history.InstallDay, decoded.History.InstallDay);
        Assert.True(decoded.History.HasSameContentAs(history));
        Assert.Equal(27, decoded.History.Readings.Count);
        Assert.False(decoded.HasUnreadParts);
    }

    [Fact]
    public void ValuesComeBackExactlyNotRounded()
    {
        double[] awkward = [0.1 + 0.2, 1.0 / 3.0, 182.40000000000001, 1e15 + 0.5, -0.0000001, 61.123456789012345];
        var readings = awkward.Select((value, index) =>
            BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay.AddDays(index), value)).ToList();

        var back = BodyBackupCodec.Read(BodyBackupCodec.Write(BodyHistory.Create(null, readings))).History;

        Assert.Equal(readings.Select(reading => reading.Value), back.Readings.Select(reading => reading.Value));
    }

    [Fact]
    public void AYearsReadingsOfEverySignalSurviveAFewTimesOver()
    {
        var readings = BackupInvented.EverySignal(days: 365 * 3);

        var back = BodyBackupCodec.Read(BodyBackupCodec.Write(BodyHistory.Create(BackupInvented.InstallDay, readings))).History;

        Assert.Equal(readings.Count, back.Readings.Count);
        Assert.True(back.HasSameContentAs(BodyHistory.Create(BackupInvented.InstallDay, readings)));
    }

    [Fact]
    public void TheLaterOfTwoReadingsOfADayWins()
    {
        const string text =
            """{"version":1,"readings":{"weight":[{"day":"2026-11-02","value":180.5},{"day":"2026-11-02","value":181.5}]}}""";

        var reading = Assert.Single(BodyBackupCodec.Read(text).History.Readings);

        Assert.Equal(181.5, reading.Value);
    }

    [Fact]
    public void AReadingThatIsNotANumberIsNeverWritten()
    {
        var history = BodyHistory.Create(
            null,
            [
                BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, double.NaN),
                BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay.AddDays(1), double.PositiveInfinity),
                BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay.AddDays(2), 180.5),
            ]);

        var back = BodyBackupCodec.Read(BodyBackupCodec.Write(history)).History;

        Assert.Equal(180.5, Assert.Single(back.Readings).Value);
    }

    // ---- a backup from a newer build ----

    [Fact]
    public void ASignalThisBuildDoesNotKnowIsSkippedNotFatal()
    {
        const string text =
            """{"version":1,"installDay":"2026-11-01","readings":{"weight":[{"day":"2026-11-02","value":180.5}],"blood-pressure":[{"day":"2026-11-02","value":120}]}}""";

        var decoded = BodyBackupCodec.Read(text);

        var reading = Assert.Single(decoded.History.Readings);
        Assert.Equal(BodySignal.Weight, reading.Signal);
        Assert.True(decoded.HasUnreadParts);
    }

    [Fact]
    public void ANewerFormatIsReadAsFarAsItCanBeAndSaysSo()
    {
        const string text =
            """{"version":2,"installDay":"2026-11-01","units":"metric","readings":{"steps":[{"day":"2026-11-02","value":5000,"source":"watch"}]}}""";

        var decoded = BodyBackupCodec.Read(text);

        Assert.Equal(5000, Assert.Single(decoded.History.Readings).Value);
        Assert.Equal(2, decoded.FormatVersion);
        Assert.True(decoded.HasUnreadParts);
    }

    [Fact]
    public void ThisBuildsOwnFormatHasNothingUnread()
    {
        var decoded = BodyBackupCodec.Read(BodyBackupCodec.Write(Everything()));

        Assert.Equal(BodyBackupCodec.CurrentVersion, decoded.FormatVersion);
        Assert.Equal(1, BodyBackupCodec.CurrentVersion);
        Assert.False(decoded.HasUnreadParts);
    }

    // ---- a bad file is a typed error ----

    public static IEnumerable<object[]> BadFiles() =>
    [
        [""],
        ["   "],
        ["{"],
        ["not json at all"],
        ["[]"],
        ["null"],
        ["{}"],
        ["""{"version":"1","readings":{}}"""],
        ["""{"version":0,"readings":{}}"""],
        ["""{"version":1.5,"readings":{}}"""],
        ["""{"version":1}"""],
        ["""{"version":1,"readings":[]}"""],
        ["""{"version":1,"installDay":"yesterday","readings":{}}"""],
        ["""{"version":1,"installDay":5,"readings":{}}"""],
        ["""{"version":1,"readings":{"weight":{}}}"""],
        ["""{"version":1,"readings":{"weight":[5]}}"""],
        ["""{"version":1,"readings":{"weight":[{"value":180.5}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":"2026-13-45","value":180.5}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":"11/02/2026","value":180.5}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":2,"value":180.5}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":"2026-11-02"}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":"2026-11-02","value":"heavy"}]}}"""],
        ["""{"version":1,"readings":{"weight":[{"day":"2026-11-02","value":1e999}]}}"""],
    ];

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void AGarbledFileIsATypedError(string text)
    {
        Assert.Throws<BodyBackupFormatException>(() => BodyBackupCodec.Read(text));
    }

    [Fact]
    public void ABadFileKeepsItsInnerExceptionForDebugging()
    {
        var error = Assert.Throws<BodyBackupFormatException>(() => BodyBackupCodec.Read("{ this is cut off"));

        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public void TheErrorNeverEchoesWhatWasInTheFile()
    {
        // Valid JSON, with a value that is wrong and that looks like a reading: the message may say
        // what is wrong, never what was there.
        var error = Assert.Throws<BodyBackupFormatException>(() => BodyBackupCodec.Read(
            """{"version":1,"readings":{"weight":[{"day":"2026-11-02","value":"183.25-heavy"}]}}"""));

        Assert.DoesNotContain("183.25", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("heavy", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-11-02", error.Message, StringComparison.Ordinal);
    }
}
