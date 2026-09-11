using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// The stored shape of Foreword's own tasks.
///
/// <para>Written down now, before any phone has stored one. The calendar mapping taught this the
/// expensive way: enums must go to the wire as <b>words</b>, because the day a serialiser is
/// constructed without <c>JsonStringEnumConverter</c> — or a reader is — the document written by the
/// other one comes back empty, and the failure is silent. Pinning it while nothing is stored costs
/// nothing; pinning it afterwards is a migration.</para>
///
/// <para>The case that matters is written as <b>a document as it would sit on the phone</b> rather
/// than as a round trip. A round trip passes happily with numbers at both ends, which is precisely
/// the bug.</para>
/// </summary>
public class TaskStoreCodecTests
{
    [Fact]
    public void EveryEnumIsWrittenAsAWord()
    {
        var lawn = new ForewordTask
        {
            Id = "lawn",
            Title = "Mow the lawn",
            Kind = TaskKind.Smart,
            Window = TaskWindow.Weekend,
            Prefers = DayOfWeek.Saturday,
            Repeat = Repeat.Weekly,
            IfNoDay = NoDayFallback.SkipTheWeek,
        };

        var written = CalendarMappingCodec.Write(lawn);

        Assert.Contains("\"Smart\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Weekend\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Saturday\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Weekly\"", written, StringComparison.Ordinal);
        Assert.Contains("\"SkipTheWeek\"", written, StringComparison.Ordinal);
    }

    /// <summary>A document in the shape the phone holds reads back whole.</summary>
    [Fact]
    public void ADocumentAsItSitsOnThePhoneReadsBack()
    {
        const string stored = """
        [
          {
            "Id": "lawn", "Title": "Mow the lawn", "Kind": "Smart",
            "Window": "Weekend", "Prefers": "Saturday", "DryHoursNeeded": 3,
            "Season": { "FromMonth": 4, "ToMonth": 10 },
            "IfNoDay": "PrintAnyway", "Written": "mow the lawn this weekend if dry"
          },
          { "Id": "vitamins", "Title": "Vitamins", "Kind": "Rule", "Repeat": "Daily" }
        ]
        """;

        var tasks = CalendarMappingCodec.Read<List<ForewordTask>>(stored);

        Assert.NotNull(tasks);
        Assert.Equal(2, tasks!.Count);

        var lawn = tasks[0];
        Assert.Equal(TaskKind.Smart, lawn.Kind);
        Assert.Equal(TaskWindow.Weekend, lawn.Window);
        Assert.Equal(DayOfWeek.Saturday, lawn.Prefers);
        Assert.Equal(3, lawn.DryHoursNeeded);
        Assert.Equal(new SeasonRange(4, 10), lawn.Season);
        Assert.Equal(NoDayFallback.PrintAnyway, lawn.IfNoDay);

        Assert.Equal(TaskKind.Rule, tasks[1].Kind);
        Assert.Equal(Repeat.Daily, tasks[1].Repeat);
    }

    /// <summary>
    /// A task stored before a field existed still reads, with the field at its default rather than
    /// the whole list rejected. One task written by an older build must never cost the others.
    /// </summary>
    [Fact]
    public void ATaskWrittenBeforeAFieldExistedStillReads()
    {
        var tasks = CalendarMappingCodec.Read<List<ForewordTask>>(
            """[{ "Id": "v", "Title": "Vitamins", "Kind": "Rule" }]""");

        Assert.Single(tasks!);
        Assert.True(tasks![0].Season.IsAllYear);
        Assert.False(tasks[0].Unparsed);
    }

    /// <summary>The away pause round-trips too — it is stored beside the tasks and read at 6:30.</summary>
    [Fact]
    public void TheAwayPauseRoundTrips()
    {
        var away = new AwayPause
        {
            From = new DateOnly(2026, 10, 12),
            Until = new DateOnly(2026, 10, 19),
            PauseWorkBlocks = false,
            StillDeliver = true,
        };

        var back = CalendarMappingCodec.Read<AwayPause>(CalendarMappingCodec.Write(away));

        Assert.Equal(away.From, back!.From);
        Assert.Equal(away.Until, back.Until);
        Assert.False(back.PauseWorkBlocks);
        Assert.True(back.StillDeliver);
    }

    /// <summary>
    /// A garbled document reads as absent rather than throwing. The owner can do something with an
    /// empty list; they can do nothing with a crashed delivery.
    /// </summary>
    [Fact]
    public void AGarbledDocumentReadsAsNothing()
    {
        Assert.Null(CalendarMappingCodec.Read<List<ForewordTask>>("{not json"));
    }
}
