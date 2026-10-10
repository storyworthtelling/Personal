using System.Globalization;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;

namespace Pulp.Domain.Settings;

/// <summary>
/// Settings → Discover: the stack's size (D106 — configurable, default three films and two shows),
/// stepped a card at a time. Either pile may be emptied, up to <see cref="Most"/> each, but the last
/// card of the whole stack stays: a stack of nothing would read as "used up" every Friday (D112).
/// </summary>
public static class StackSizeChoice
{
    /// <summary>The most one pile holds. A choice, not a measure: past it the pile is a list, not a stack.</summary>
    public const int Most = 9;

    private const string Dot = " · ";

    public static bool CanStep(StackSizes sizes, TitleKind kind, int delta)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        if (delta == 0 || kind is not (TitleKind.Film or TitleKind.Show))
        {
            return false;
        }

        var films = sizes.Films + (kind == TitleKind.Film ? delta : 0);
        var shows = sizes.Shows + (kind == TitleKind.Show ? delta : 0);
        return Within(films, shows);
    }

    public static StackSizes Step(StackSizes sizes, TitleKind kind, int delta)
    {
        if (!CanStep(sizes, kind, delta))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta), delta, $"A {kind} pile of {sizes.For(kind)} cannot step by {delta}: each pile holds 0 to {Most} and the stack keeps one card.");
        }

        return kind == TitleKind.Film
            ? new StackSizes(sizes.Films + delta, sizes.Shows)
            : new StackSizes(sizes.Films, sizes.Shows + delta);
    }

    /// <summary>A stored choice read back; anything missing or out of bounds is the default, never clamped into a size nobody chose.</summary>
    public static StackSizes Read(int? films, int? shows) =>
        films is { } f && shows is { } s && Within(f, s) ? new StackSizes(f, s) : StackSizes.Default;

    /// <summary>The Discover group's header line, as drawn: "3 films · 2 shows".</summary>
    public static string Summary(StackSizes sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        return Pile(TitleKind.Film, sizes.Films) + Dot + Pile(TitleKind.Show, sizes.Shows);
    }

    /// <summary>One pile's count in words: "3 films", "1 show", "No films".</summary>
    public static string Pile(TitleKind kind, int count)
    {
        var noun = kind switch
        {
            TitleKind.Film => "film",
            TitleKind.Show => "show",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A title kind the stack has no pile for."),
        };

        return count == 0
            ? "No " + noun + "s"
            : count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
    }

    private static bool Within(int films, int shows) =>
        films is >= 0 and <= Most && shows is >= 0 and <= Most && films + shows >= 1;
}

/// <summary>
/// Where this phone keeps its stack size (D106): install-local preferences, never the household file —
/// the stack is this phone's adult's (D23, D283). Frozen: an upgraded phone finds its choice by these
/// names (<c>Pulp.Tests/Settings/StackSizePreferenceTests</c>).
/// </summary>
public static class StackSizePreference
{
    public const string FileName = "pulp-stack-size";

    public const string FilmsKey = "films";

    public const string ShowsKey = "shows";
}
