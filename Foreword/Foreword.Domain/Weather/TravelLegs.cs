using Foreword.Domain.Calendar;

namespace Foreword.Domain.Weather;

/// <summary>
/// What a router measured: the legs it could answer for, ready to become a
/// <see cref="TravelTable"/>.
/// </summary>
public sealed record TravelLegs(IReadOnlyList<(string From, string To, int Minutes)> Legs)
{
    public static TravelLegs None { get; } = new([]);

    public TravelTable AsTable(string home) => TravelTable.Of(home, Legs);
}
