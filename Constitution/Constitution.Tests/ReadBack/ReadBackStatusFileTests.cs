using Constitution.Domain.Diagnostics;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>The last morning's outcome, kept for the Diagnostics export: one closed word and a time, nothing else.</summary>
public sealed class ReadBackStatusFileTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-status-");
    private readonly DateTimeOffset at = new(2026, 11, 4, 7, 15, 0, TimeSpan.FromHours(-5));

    private string PathOf() => Path.Combine(folder.FullName, "read-back.json");

    private ReadBackStatusFile Make() => new(PathOf(), () => at);

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void NeverRanBeforeAnythingIsRecorded() => Assert.Equal(DiagnosticsOutcome.NeverRan, Make().LastOutcome());

    [Theory]
    [InlineData(DiagnosticsOutcome.Succeeded)]
    [InlineData(DiagnosticsOutcome.FoundNothingRead)]
    [InlineData(DiagnosticsOutcome.Refused)]
    [InlineData(DiagnosticsOutcome.Unreachable)]
    [InlineData(DiagnosticsOutcome.Partial)]
    public void WhatWasRecordedIsWhatIsRead(DiagnosticsOutcome outcome)
    {
        Make().Record(outcome);

        Assert.Equal(outcome, Make().LastOutcome());
    }

    [Fact]
    public void ALaterOutcomeReplacesTheEarlier()
    {
        var status = Make();
        status.Record(DiagnosticsOutcome.Refused);
        status.Record(DiagnosticsOutcome.Succeeded);

        Assert.Equal(DiagnosticsOutcome.Succeeded, status.LastOutcome());
    }

    [Fact]
    public void ADamagedFileSaysItNeverRan()
    {
        File.WriteAllText(PathOf(), "{ not json");

        Assert.Equal(DiagnosticsOutcome.NeverRan, Make().LastOutcome());
    }

    [Fact]
    public void AnUnknownWordSaysItNeverRan()
    {
        File.WriteAllText(PathOf(), "{\"outcome\":\"Exploded\",\"at\":\"2026-11-04T07:15:00-05:00\"}");

        Assert.Equal(DiagnosticsOutcome.NeverRan, Make().LastOutcome());
    }

    [Fact]
    public void ANumberThatIsNotAnOutcomeSaysItNeverRan()
    {
        // Enum parsing accepts any digits; a number that names no outcome must not print in an export.
        File.WriteAllText(PathOf(), "{\"outcome\":\"99\",\"at\":\"2026-11-04T07:15:00-05:00\"}");

        Assert.Equal(DiagnosticsOutcome.NeverRan, Make().LastOutcome());
    }

    [Fact]
    public void AnUndefinedOutcomeIsRefusedWhenRecorded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make().Record((DiagnosticsOutcome)99));
    }

    [Fact]
    public void TheFileHoldsTheWordAndTheTimeAndNothingElse()
    {
        Make().Record(DiagnosticsOutcome.FoundNothingRead);

        var text = File.ReadAllText(PathOf());
        Assert.Contains("FoundNothingRead", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-04", text, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateObject().Count());
    }
}
