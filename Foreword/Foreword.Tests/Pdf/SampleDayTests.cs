using Foreword.Domain.Models;
using Foreword.Domain.Tasks;
using Foreword.Domain.Weather;
using Foreword.Infrastructure.Pdf;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// Writes a realistic Day document to disk when FOREWORD_SAMPLE names a path, so the sheets can be
/// looked at rather than only asserted about. Skipped otherwise — it is a tool, not a check.
/// </summary>
public class SampleDayTests
{
    private static PrintedTask Printed(string title, DateOnly? due = null, string note = "",
        TaskKind kind = TaskKind.Once) =>
        new(new ForewordTask { Id = title, Title = title, Kind = kind, Due = due }, note);

    [Fact]
    public void WriteSample()
    {
        var path = Environment.GetEnvironmentVariable("FOREWORD_SAMPLE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var day = new DateOnly(2026, 9, 13);

        CalendarEvent At(PersonColumn column, int from, int to, string title,
            string? where = null, EventKind kind = EventKind.Normal) => new()
        {
            Title = title,
            Location = where,
            Columns = [column],
            Kind = kind,
            Start = day.ToDateTime(new TimeOnly(from, 0)),
            End = day.ToDateTime(new TimeOnly(to, 0)),
        };

        var agenda = new DayAgenda
        {
            Date = day,
            Events =
            [
                At(PersonColumn.Matt, 9, 12, "Work", "The office", EventKind.WorkBlock),
                At(PersonColumn.Matt, 9, 10, "Standup"),
                At(PersonColumn.Matt, 13, 17, "Work", "The office", EventKind.WorkBlock),
                At(PersonColumn.Matt, 14, 15, "Release review"),
                At(PersonColumn.Ashley, 9, 15, "Work", null, EventKind.WorkBlock),
                At(PersonColumn.Ashley, 10, 11, "Team sync"),
                At(PersonColumn.Ashley, 15, 16, "Vet — Ruby"),
                At(PersonColumn.Gwen, 8, 15, "School", "The school"),
                At(PersonColumn.Gwen, 16, 17, "Soccer practice", "Veterans Park"),
                At(PersonColumn.Eva, 13, 14, "Piano"),
                At(PersonColumn.Watch, 19, 22, "Eagles v Cowboys"),
            ],
            Travel = Foreword.Domain.Calendar.TravelTable.Of("Home",
            [
                ("Home", "The office", 15),
                ("Home", "The school", 12),
                ("The school", "Veterans Park", 12),
                ("The office", "Veterans Park", 40),
            ]),
        };

        var document = new DayDocument
        {
            Date = day,
            Agenda = agenda,
            Voice = "Two of Ashley\u2019s land on top of each other at half three, and the lawn has moved to today.",
            ChecklistVoice = "Eight things, and the lawn is the only one that will take you an hour.",
            Outlook =
            [
                At(PersonColumn.Eva, 16, 17, "Eva \u2014 orthodontist"),
                At(PersonColumn.Gwen, 18, 19, "Gwen \u2014 game"),
            ],
            Tasks =
            [
                Printed("Vitamins", kind: TaskKind.Rule),
                Printed("Mow the lawn", note: "moved from Saturday \u2014 not enough dry hours",
                    kind: TaskKind.Smart),
                Printed("Read the brief", kind: TaskKind.Rule),
                Printed("Call Mom"),
                Printed("Book Eva\u2019s orthodontist follow-up", due: day.AddDays(5)),
                Printed("Pick up the dry cleaning"),
                Printed("Order Gwen\u2019s cleats"),
                Printed("Bins out", note: "collection is Monday", kind: TaskKind.Rule),
                Printed("Sign Gwen\u2019s permission slip tonight"),
            ],
            Weather = new WeatherReport
            {
                Today = new TodayWeather
                {
                    Date = day,
                    High = 78,
                    Low = 57,
                    HighAt = day.ToDateTime(new TimeOnly(15, 0)),
                    Condition = "Clearing",
                    Sun = new SunAndMoon(new TimeOnly(6, 32), new TimeOnly(19, 21), "WANING_GIBBOUS"),
                    Hours =
                    [
                        .. new[] { 57, 57, 58, 60, 63, 67, 70, 73, 76, 78, 77, 75, 72, 69, 66, 63, 60 }
                            .Select((degrees, i) => new HourWeather(
                                day.ToDateTime(new TimeOnly(6 + i, 0)), degrees,
                                i switch { 0 => 70, 1 => 80, 2 => 55, 3 => 30, _ => 0 })),
                    ],
                },
                Week =
                [
                    new WeekDay(day, "Clearing", 78, 57, 30),
                    new WeekDay(day.AddDays(1), "Sunny", 81, 60, 5),
                    new WeekDay(day.AddDays(2), "Sunny", 83, 62, 5),
                    new WeekDay(day.AddDays(3), "Cloud", 76, 61, 20),
                    new WeekDay(day.AddDays(4), "Showers", 71, 58, 70),
                    new WeekDay(day.AddDays(5), "Showers", 69, 55, 60),
                    new WeekDay(day.AddDays(6), "Clearing", 74, 54, 25),
                ],
                Allergy =
                [
                    new AllergyReading("Ragweed", "High", 4),
                    new AllergyReading("Grass", "Low", 1),
                    new AllergyReading("Tree", "None", 0),
                    new AllergyReading("Air quality", "50 \u00b7 Moderate", 2),
                ],
            },
        };

        File.WriteAllBytes(path!, new DayPdfComposer().Compose(document));
    }
}
