using SkiaSharp;
using Foreword.Domain.Time;
using Foreword.Domain.Weather;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// Page 2 of The Day (D2): today in detail on the timeline's own 6am-10pm axis, the week under it,
/// what is in the air, and the sun.
///
/// <para>The axis is shared deliberately — an event on page 1 and the weather at that hour sit at
/// the same height on the two sheets, so the pages can be compared by holding one behind the other.
/// The green window is the longest dry stretch, which is the question the page is actually asked:
/// not "will it rain", which the bars answer, but "when can the lawn be cut".</para>
///
/// <para>Every part is optional, because each came from a different call that can fail on its own.
/// A page with nothing on it says so in one sentence rather than drawing an empty chart, which
/// would read as a forecast of nothing rather than as no forecast.</para>
/// </summary>
internal static class WeatherPage
{
    // The same window the timeline draws, for the same reason.
    private const int FirstHour = 6;
    private const int LastHour = 22;
    private const int HourSpan = LastHour - FirstHour;

    private const double ChartHeight = 120;
    private const double PrecipHeight = 20;
    private const double AxisHeight = 12;
    private const double Gutter = 30;
    private const double SectionGap = 12;

    private static readonly SKColor Identity = AppIdentityTokens.ForewordSunrise;

    private static readonly SKColor DryFill =
        PaperPalette.ResolveRole(ColorRole.Positive, ColorTier.Wash, UiColorProfile.DocumentColor);
    private static readonly SKColor DryMark =
        PaperPalette.ResolveRole(ColorRole.Positive, ColorTier.Mark, UiColorProfile.DocumentColor);
    private static readonly SKColor RainMark =
        PaperPalette.ResolveRole(ColorRole.InProgress, ColorTier.Fill, UiColorProfile.DocumentColor);

    public static void Draw(SkiaPageLayout layout, WeatherReport report)
    {
        var sectionFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);

        if (report.IsEmpty)
        {
            // Honest rather than blank. A drawn chart with nothing in it reads as a forecast of
            // nothing, which is a different and worse claim than "no forecast came back".
            layout.Advance(SectionGap);
            layout.DrawText("No forecast came back this morning.",
                SkiaPageLayout.Sans(PaperTypeScale.Body), SkiaPageLayout.Margin, layout.Y,
                DayPageFurniture.ContentWidth(layout), 20, TextAlign.TopLeft, SkiaPageLayout.InkMuted);
            return;
        }

        if (report.Today is { } today)
        {
            layout.Advance(SectionGap);
            layout.DrawText(WeatherHeadline.Describe(today), sectionFont,
                SkiaPageLayout.Margin, layout.Y, DayPageFurniture.ContentWidth(layout), 14,
                TextAlign.TopLeft, SkiaPageLayout.InkSoft);
            layout.Advance(16);

            DrawChart(layout, today);
        }

        if (report.Allergy.Count > 0)
        {
            layout.Advance(SectionGap);
            Section(layout, sectionFont, "Allergy & air");
            DrawAllergy(layout, report.Allergy);
        }

        if (report.Week.Count > 0)
        {
            layout.Advance(SectionGap);
            Section(layout, sectionFont, "The week");
            DrawWeek(layout, report.Week);
        }

        if (report.Today is { } sunDay)
        {
            DrawSunAndMoon(layout, sunDay);
        }
    }

    private static void Section(SkiaPageLayout layout, SKFont font, string title)
    {
        layout.DrawText(title, font, SkiaPageLayout.Margin, layout.Y,
            DayPageFurniture.ContentWidth(layout), 12, TextAlign.TopLeft, SkiaPageLayout.InkSoft);
        layout.Advance(14);
    }

    /// <summary>
    /// The temperature curve, the dry window behind it, the rain bars under it, and the hours.
    /// </summary>
    private static void DrawChart(SkiaPageLayout layout, TodayWeather today)
    {
        var left = SkiaPageLayout.Margin + Gutter;
        var right = DayPageFurniture.ContentRight(layout);
        var width = right - left;
        var top = layout.Y;

        if (today.Hours.Count < 2 || width <= 0)
        {
            return;
        }

        var labelFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        // The scale is padded past the day's own range so the curve never touches the frame; a line
        // that runs along the top edge cannot be read as a peak.
        var warmest = today.Hours.Max(h => h.Degrees);
        var coldest = today.Hours.Min(h => h.Degrees);
        var high = warmest + 7;
        var low = coldest - 7;
        var span = Math.Max(1, high - low);

        double X(DateTime hour) => left + (hour.Hour - FirstHour) / (double)HourSpan * width;
        double Y(int degrees) => top + (high - degrees) / (double)span * ChartHeight;

        // The dry window, behind everything, because it is a condition of the day rather than a
        // reading in it.
        if (DryWindow.Find(today.Hours) is { } dry)
        {
            var dryLeft = X(dry.Start);
            var dryRight = Math.Min(right, X(dry.End) + width / HourSpan);
            layout.DrawRect(dryLeft, top, dryRight - dryLeft, ChartHeight, fill: DryFill);
            layout.DrawLine(dryLeft, top, dryLeft, top + ChartHeight, DryMark, 0.6f);
            layout.DrawLine(dryRight, top, dryRight, top + ChartHeight, DryMark, 0.6f);

            layout.DrawText($"dry from {TimeFormats.Hour12(dry.Start)}", labelFont,
                dryLeft + 4, top + 4, dryRight - dryLeft - 6, 12, TextAlign.TopLeft, DryMark);
        }

        // Three readings up the side, which is as many as a 120-point chart can carry legibly.
        foreach (var degrees in new[] { high, (high + low) / 2, low })
        {
            layout.DrawText($"{degrees}°", labelFont, SkiaPageLayout.Margin, Y(degrees) - 5,
                Gutter - 4, 12, TextAlign.TopRight, SkiaPageLayout.InkMuted);
        }

        // The curve itself, in the identity hue — the one line on the page that is the day's own
        // shape rather than a reading of it.
        for (var i = 1; i < today.Hours.Count; i++)
        {
            var a = today.Hours[i - 1];
            var b = today.Hours[i];
            layout.DrawLine(X(a.Hour), Y(a.Degrees), X(b.Hour), Y(b.Degrees), Identity, 1.6f);
        }

        // The peak, marked, so the "at 3pm" in the sentence above has something to point at.
        if (today.HighAt is { } peak)
        {
            var at = today.Hours.FirstOrDefault(h => h.Hour == peak);
            layout.DrawRect(X(peak) - 2, Y(at.Degrees) - 2, 4, 4, fill: Identity);
        }

        // Rain, as bars under the curve rather than a second line on it: a chance is not a
        // temperature and drawing both as curves would invite reading one off the other's axis.
        var precipTop = top + ChartHeight + 2;
        var barWidth = Math.Max(2, width / HourSpan * 0.45);
        foreach (var hour in today.Hours.Where(h => h.PrecipitationPercent > 0))
        {
            var height = hour.PrecipitationPercent / 100.0 * PrecipHeight;
            layout.DrawRect(X(hour.Hour) - barWidth / 2, precipTop + PrecipHeight - height,
                barWidth, height, fill: RainMark);
        }

        // The hours, every second one, because seventeen labels on this width is a grey smear.
        var axisTop = precipTop + PrecipHeight;
        layout.DrawLine(left, axisTop, right, axisTop, SkiaPageLayout.Hairline, 0.5f);

        for (var hour = FirstHour; hour <= LastHour; hour += 2)
        {
            var at = today.Date.ToDateTime(new TimeOnly(hour, 0));
            layout.DrawText(TimeFormats.Hour12(at), labelFont, X(at) - 12, axisTop + 2,
                24, 12, TextAlign.Center, SkiaPageLayout.InkMuted);
        }

        layout.MoveTo(axisTop + AxisHeight + 4);
    }

    /// <summary>
    /// Four readings, each with a five-step meter. September in New Jersey is ragweed season, which
    /// is why ragweed leads rather than the alphabet.
    /// </summary>
    private static void DrawAllergy(SkiaPageLayout layout, IReadOnlyList<AllergyReading> readings)
    {
        const double Gap = 10;
        const double Height = 34;
        const double StepHeight = 5;

        var nameFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var valueFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);

        var left = SkiaPageLayout.Margin;
        var width = (DayPageFurniture.ContentWidth(layout) - Gap * (readings.Count - 1)) / readings.Count;
        var top = layout.Y;

        for (var i = 0; i < readings.Count; i++)
        {
            var reading = readings[i];
            var x = left + i * (width + Gap);
            var hue = SeverityMark(reading.Value);

            layout.DrawRect(x, top, width, Height, stroke: SkiaPageLayout.Hairline, strokeWidth: 0.5f);
            layout.DrawText(reading.Name, nameFont, x + 5, top + 3, width - 10, 11, TextAlign.TopLeft);
            layout.DrawText(reading.Category, valueFont, x + 5, top + 13, width - 10, 11,
                TextAlign.TopLeft, hue);

            // Five steps, filled to the reading. A meter rather than a number because "4" means
            // nothing without the scale beside it, and the scale is the same for all four.
            var stepWidth = (width - 10 - (AllergyReading.Steps - 1) * 2.0) / AllergyReading.Steps;
            for (var step = 0; step < AllergyReading.Steps; step++)
            {
                layout.DrawRect(x + 5 + step * (stepWidth + 2), top + Height - StepHeight - 4,
                    stepWidth, StepHeight,
                    fill: step < reading.Value ? hue : SkiaPageLayout.Hairline);
            }
        }

        layout.MoveTo(top + Height);
    }

    /// <summary>
    /// The severity hue. It is a role rather than a fixed colour because the reading means
    /// something: none is resolved, high is a problem, and the page should be readable by the
    /// meaning of its colours alone.
    /// </summary>
    private static SKColor SeverityMark(int value) =>
        PaperPalette.ResolveRole(
            value switch
            {
                <= 0 => ColorRole.Positive,
                <= 2 => ColorRole.Caution,
                _ => ColorRole.Problem,
            },
            ColorTier.Mark, UiColorProfile.DocumentColor);

    private static void DrawWeek(SkiaPageLayout layout, IReadOnlyList<WeekDay> week)
    {
        const double Gap = 6;
        const double Height = 46;

        var dayFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var conditionFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var tempFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);

        var left = SkiaPageLayout.Margin;
        var width = (DayPageFurniture.ContentWidth(layout) - Gap * (week.Count - 1)) / week.Count;
        var top = layout.Y;

        for (var i = 0; i < week.Count; i++)
        {
            var day = week[i];
            var x = left + i * (width + Gap);

            // Today is outlined in the identity hue, which is the only thing distinguishing it —
            // the strip is otherwise seven identical cards, and a strip where today is not obvious
            // is a strip that has to be counted.
            layout.DrawRect(x, top, width, Height,
                stroke: i == 0 ? Identity : SkiaPageLayout.Hairline,
                strokeWidth: i == 0 ? 1.6f : 0.5f);

            layout.DrawText(day.Date.ToString("ddd"), dayFont, x, top + 4, width, 11, TextAlign.Center);
            layout.DrawText(day.Condition, conditionFont, x + 2, top + 15, width - 4, 11,
                TextAlign.Center, SkiaPageLayout.InkSoft);
            layout.DrawText($"{day.High}°  {day.Low}°", tempFont, x, top + 26, width, 12,
                TextAlign.Center);
            layout.DrawText($"{day.PrecipitationPercent}%", conditionFont, x, top + Height - 12,
                width, 11, TextAlign.Center, RainMark);
        }

        layout.MoveTo(top + Height);
    }

    /// <summary>
    /// The foot. Daylight is here because a smart task can require it, so the number has a job
    /// beyond being interesting.
    /// </summary>
    private static void DrawSunAndMoon(SkiaPageLayout layout, TodayWeather today)
    {
        var parts = new List<string>();

        if (today.Sun.Sunrise is { } rise) parts.Add($"Sunrise {Clock(rise)}");
        if (today.Sun.Sunset is { } set) parts.Add($"Sunset {Clock(set)}");
        if (SunLine.Daylight(today.Sun) is { Length: > 0 } daylight) parts.Add(daylight);
        if (SunLine.Moon(today.Sun.MoonPhase) is { Length: > 0 } moon) parts.Add(moon);

        if (parts.Count == 0)
        {
            return;
        }

        var labelFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        var lineFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);

        var top = layout.BottomY - 28;
        layout.DrawLine(SkiaPageLayout.Margin, top, DayPageFurniture.ContentRight(layout), top,
            SkiaPageLayout.Hairline, 0.5f);

        layout.DrawText("Sun & moon", labelFont, SkiaPageLayout.Margin, top + 4,
            DayPageFurniture.ContentWidth(layout), 12, TextAlign.TopLeft, SkiaPageLayout.InkSoft);
        layout.DrawText(string.Join("   ·   ", parts), lineFont, SkiaPageLayout.Margin, top + 15,
            DayPageFurniture.ContentWidth(layout), 12, TextAlign.TopLeft);
    }

    private static string Clock(TimeOnly time) =>
        TimeFormats.Clock12(DateTime.Today.Add(time.ToTimeSpan())).Replace(" ", string.Empty);
}
