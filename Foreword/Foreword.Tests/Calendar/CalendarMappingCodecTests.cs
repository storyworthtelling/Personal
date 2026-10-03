using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The stored calendar settings survive the screens replacing the editor that wrote them.
///
/// <para>The document already exists on the owner's phone and in the secrets vault. Reading it into
/// a different shape would lose their configuration silently — the morning would simply print into
/// one column with nothing having failed.</para>
/// </summary>
public class CalendarMappingCodecTests
{
    /// <summary>
    /// The load-bearing case, written as the document <b>actually on the phone</b> rather than as a
    /// round trip. A round trip alone passes happily with the enum written as a number at both ends,
    /// which is precisely the bug: the old parser registered a string-enum converter, so every
    /// column in every stored document is the word "Alex", never the number 0.
    /// </summary>
    [Fact]
    public void ADocumentWrittenByTheOldEditorStillReads()
    {
        const string stored = """
        {
          "CalendarColumns": { "sam-work@group": "Sam", "redsox@group": "Watch" },
          "FamilyCalendarId": "family@group",
          "DefaultColumn": "Alex",
          "RileyKeywords": ["Riley"],
          "JoKeywords": ["Jo"]
        }
        """;

        var options = CalendarMappingCodec.Read<CalendarMappingOptions>(stored);

        Assert.NotNull(options);
        Assert.Equal(Household.Sam, options!.CalendarColumns["sam-work@group"]);
        Assert.Equal(PersonColumn.Watch, options.CalendarColumns["redsox@group"]);
        Assert.Equal("family@group", options.FamilyCalendarId);
        Assert.Equal(Household.Alex, options.DefaultColumn);
    }

    /// <summary>
    /// And what the screens write is readable by the same rules — columns as words, so a document
    /// this app wrote today is one the old parser would also have understood.
    /// </summary>
    [Fact]
    public void WhatTheScreensWriteUsesWordsForColumns()
    {
        var written = CalendarMappingCodec.Write(new CalendarMappingOptions
        {
            CalendarColumns = { ["a@group"] = Household.Riley },
        });

        Assert.Contains("\"Riley\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"a@group\":2", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The new per-calendar rules are additive: an older document simply leaves them empty rather
    /// than failing to read.
    /// </summary>
    [Fact]
    public void AnOlderDocumentLeavesTheNewRulesEmpty()
    {
        var options = CalendarMappingCodec.Read<CalendarMappingOptions>(
            """{ "FamilyCalendarId": "family@group" }""");

        Assert.NotNull(options);
        Assert.Empty(options!.SplitCalendars);
        Assert.Empty(options.EveryoneCalendars);
        Assert.Empty(options.NotPrintedCalendars);
    }

    /// <summary>
    /// Addresses are keyed by person, and the key is a word too — a dictionary written with numeric
    /// keys reads back as an empty address book, which would quietly disable the guest rule that
    /// D31 reads first.
    /// </summary>
    [Fact]
    public void PersonKeyedDictionariesRoundTripAsWords()
    {
        var written = CalendarMappingCodec.Write(new CalendarMappingOptions
        {
            PersonAddresses = { [Household.Jo] = ["jo@example.com"] },
        });

        Assert.Contains("\"Jo\"", written, StringComparison.Ordinal);

        var back = CalendarMappingCodec.Read<CalendarMappingOptions>(written);
        Assert.Equal(["jo@example.com"], back!.PersonAddresses[Household.Jo]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void AGarbledOrEmptyDocumentReadsAsAbsentRatherThanThrowing(string json)
    {
        Assert.Null(CalendarMappingCodec.Read<CalendarMappingOptions>(json));
    }
}
