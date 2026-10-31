using System.Globalization;
using Constitution.Domain.Time;

namespace Constitution.Domain.Groceries;

/// <summary>What the household does on a holiday. Only hosting and visiting put a page on the sheet; staying home asks nothing.</summary>
public enum HolidayRole
{
    Hosting,
    Visiting,
    StayingHome,
}

/// <summary>One holiday the family calendar knows. <paramref name="Place"/> is where a visit is ("the Okafors’"); <paramref name="DinnerAt"/> is the meal's time when it has one.</summary>
public sealed record HolidayEvent(string Name, DateOnly Date, HolidayRole Role, string? Place = null, TimeOnly? DinnerAt = null);

/// <summary>What a calendar answered: whether it could be asked at all, and the holidays between two days. An unavailable calendar is not an empty one.</summary>
public sealed record HolidayEvents(bool IsAvailable, IReadOnlyList<HolidayEvent> Events)
{
    public static HolidayEvents Unavailable { get; } = new(false, []);
}

/// <summary>The seam to the family calendar's holidays. No network client lives in the domain; the default says it cannot be asked.</summary>
public interface IHolidayCalendar
{
    HolidayEvents Between(DateOnly first, DateOnly last);
}

/// <summary>The default calendar: it cannot be asked, so the sheet carries no ask-ahead page.</summary>
public sealed class UnavailableHolidayCalendar : IHolidayCalendar
{
    public HolidayEvents Between(DateOnly first, DateOnly last) => HolidayEvents.Unavailable;
}

/// <summary>The ask-ahead page's words: the holiday's name (the page title), the line saying when and where, and the lines under it.</summary>
public sealed record HolidayAsk(HolidayEvent Event, string Title, string WhenLine, IReadOnlyList<string> Lines);

/// <summary>
/// C-2.4.4 · The holiday ask-ahead. The sheet is for one planning week (Sunday to Saturday); when a holiday the household hosts or
/// visits falls in the week after it, the sheet carries one more page asking what to bring, so the paper asks early. Any other week,
/// a holiday spent at home, and a calendar that cannot be asked all give nothing.
/// </summary>
public static class HolidayAskAhead
{
    public const string AskLine = "What are we bringing? Write a dish, or leave a recipe name.";

    /// <summary>The ask for the week after <paramref name="weekStart"/>, or null.</summary>
    public static HolidayAsk? Decide(IHolidayCalendar calendar, DateOnly weekStart)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var first = weekStart.AddDays(7);
        var last = weekStart.AddDays(13);
        var found = calendar.Between(first, last);
        if (!found.IsAvailable)
        {
            return null;
        }

        var chosen = found.Events
            .Where(item => item.Role is HolidayRole.Hosting or HolidayRole.Visiting && item.Date >= first && item.Date <= last)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (chosen is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(chosen.Name))
        {
            throw new ArgumentException($"The holiday on {chosen.Date:yyyy-MM-dd} has no name; the page cannot be titled.", nameof(calendar));
        }

        var name = chosen.Name.Trim();
        var day = chosen.Date.DayOfWeek.ToString();
        return new HolidayAsk(
            chosen,
            name.Length <= MaxTitleChars ? name : "The holiday",
            WhenLine(chosen),
            [
                $"From the family calendar. Next week’s plan leaves {day}’s dinner off.",
                AskLine,
                $"Next {day}’s sheet plans the cooking time and adds it to the groceries.",
            ]);
    }

    /// <summary>The longest holiday name the page title holds; a longer one prints on the page's first line under a plain title.</summary>
    public const int MaxTitleChars = 30;

    private static string WhenLine(HolidayEvent item)
    {
        var line = item.Date.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
        if (item.Role == HolidayRole.Hosting)
        {
            line += " · hosting here";
        }
        else
        {
            line += string.IsNullOrWhiteSpace(item.Place) ? " · visiting" : " · at " + item.Place.Trim();
        }

        if (item.DinnerAt is { } time)
        {
            line += " · dinner " + TimeFormats.Clock12(item.Date.ToDateTime(time));
        }

        return line;
    }
}
