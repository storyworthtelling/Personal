using Foreword.Domain.Calendar;
using Foreword.Domain.Document;
using Foreword.Domain.Models;
using Foreword.Domain.Tasks;
using Foreword.Domain.Weather;
using Foreword.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// Nothing on any page is drawn where it cannot be read.
///
/// <para>These sheets are composed without anyone seeing them, and the two failures that produces
/// are both silent: text that runs off the edge of the paper, and text that runs under the binder
/// tabs. Neither throws, neither changes the page count, and neither shows up in a test that asks
/// whether a word is present — the word <i>is</i> present, sitting under a tab or four points past
/// the trim.</para>
///
/// <para>So the finished document is opened again and every letter's box is measured against the
/// page. It is not a substitute for looking at it, but it is the half of "does this look right"
/// that a machine can actually answer.</para>
/// </summary>
public class DayPageBoundsTests
{
    private static readonly DateOnly Day = new(2026, 9, 13);

    // The strip's left edge, in PDF coordinates: the active tab reaches one hairline further in
    // than the rest, and nothing but the tabs themselves may cross it.
    private const double TabStripLeft = 792 - SectionTabs.TabWidth - SectionTabs.Hairline;

    [Fact]
    public void NoTextRunsOffThePage()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));

        var offenders = new List<string>();

        foreach (var page in document.GetPages())
        {
            foreach (var letter in page.Letters)
            {
                var box = letter.GlyphRectangle;
                if (box.Left < 0 || box.Right > page.Width || box.Bottom < 0 || box.Top > page.Height)
                {
                    offenders.Add($"page {page.Number}: '{letter.Value}' at {box.Left:0.#},{box.Bottom:0.#}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These are drawn outside the sheet:\n" + string.Join("\n", offenders.Take(20)));
    }

    /// <summary>
    /// And nothing runs under the tabs. The tab strip is opaque — it is drawn after the page's own
    /// content — so a title that reaches it is not clipped or wrapped, it is simply painted over.
    /// </summary>
    [Fact]
    public void NoTextRunsUnderTheTabStrip()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));

        var offenders = new List<string>();

        foreach (var page in document.GetPages())
        {
            // The tabs' own labels are drawn there on purpose; they are rotated, and a rotated
            // glyph's box is the one thing in the strip that belongs in it.
            foreach (var letter in page.Letters.Where(l => l.GlyphRectangle.Right > TabStripLeft))
            {
                if (IsTabLabel(letter))
                {
                    continue;
                }

                offenders.Add($"page {page.Number}: '{letter.Value}' reaches {letter.GlyphRectangle.Right:0.#}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"These are drawn under the binder tabs (which start at {TabStripLeft:0.#}) and are "
            + "painted over rather than clipped:\n" + string.Join("\n", offenders.Take(20)));
    }

    // A tab label runs down the page rather than across it, which is what its glyph box shows.
    private static bool IsTabLabel(UglyToad.PdfPig.Content.Letter letter) =>
        Math.Abs(letter.GlyphRectangle.Rotation) > 45;

    private static byte[] Composed()
    {
        CalendarEvent At(PersonColumn column, int from, int to, string title, string? where = null) => new()
        {
            Title = title,
            Location = where,
            Columns = [column],
            Start = Day.ToDateTime(new TimeOnly(from, 0)),
            End = Day.ToDateTime(new TimeOnly(to, 0)),
        };

        return new DayPdfComposer().Compose(new DayDocument
        {
            Date = Day,
            Agenda = new DayAgenda
            {
                Date = Day,
                Events =
                [
                    At(PersonColumn.Rowan, 9, 17, "Work", "The office"),
                    At(PersonColumn.Rowan, 9, 10, "Standup with the whole platform team"),
                    At(PersonColumn.Jordan, 15, 16, "Vet — Ruby"),
                    At(PersonColumn.Wren, 8, 15, "School", "The school"),
                    At(PersonColumn.Wren, 16, 17, "Soccer practice", "Veterans Park"),
                    At(PersonColumn.Linden, 13, 14, "Piano"),
                    At(PersonColumn.Watch, 19, 22, "Eagles v Cowboys"),
                ],
                Travel = TravelTable.Of("Home", [("The school", "Veterans Park", 25)]),
            },
            Voice = "Two of Jordan's land on top of each other at half three, and the lawn has moved to today.",
            ChecklistVoice = "Eight things, and the lawn is the only one that will take you an hour.",
            Outlook = [At(PersonColumn.Linden, 16, 17, "Linden — orthodontist follow-up appointment")],
            Tasks =
            [
                Printed("Vitamins"),
                Printed("Mow the lawn", Day.AddDays(-1)),
                Printed("Book Linden's orthodontist follow-up appointment"),
                Printed("Bins out"),
            ],
            Weather = Forecast(),
        });
    }

    // A task already placed for today, which is what the page draws. The placement engine has its
    // own tests; these are about the drawing.
    private static PrintedTask Printed(string title, DateOnly? due = null, string note = "") =>
        new(new ForewordTask
        {
            Id = title,
            Title = title,
            Kind = TaskKind.Once,
            Due = due,
        }, note);

    private static WeatherReport Forecast() => new()
    {
        Today = new TodayWeather
        {
            Date = Day,
            High = 78,
            Low = 57,
            HighAt = Day.ToDateTime(new TimeOnly(15, 0)),
            Condition = "Clearing",
            Sun = new SunAndMoon(new TimeOnly(6, 32), new TimeOnly(19, 21), "WANING_GIBBOUS"),
            Hours =
            [
                .. new[] { 57, 57, 58, 60, 63, 67, 70, 73, 76, 78, 77, 75, 72, 69, 66, 63, 60 }
                    .Select((degrees, i) => new HourWeather(
                        Day.ToDateTime(new TimeOnly(6 + i, 0)), degrees,
                        i switch { 0 => 70, 1 => 80, 2 => 55, 3 => 30, _ => 0 })),
            ],
        },
        Week =
        [
            .. Enumerable.Range(0, 7).Select(i =>
                new WeekDay(Day.AddDays(i), "Showers", 78 - i, 57 + i, 30)),
        ],
        Allergy =
        [
            new AllergyReading("Ragweed", "High", 4),
            new AllergyReading("Grass", "Low", 1),
            new AllergyReading("Tree", "None", 0),
            new AllergyReading("Air quality", "50 · Moderate", 2),
        ],
    };
}
