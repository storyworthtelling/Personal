using Pulp.Domain.Core;
using Pulp.Domain.Household;

namespace Pulp.Tests.Household.Records;

/// <summary>Invented viewers, titles, services and a stamp source for the household-record tests.</summary>
internal static class RecordsFixtures
{
    public const string PhoneA = "install-a-example";
    public const string PhoneB = "install-b-example";
    public const string Tv = "install-tv-example";

    public static readonly ViewerId Robin = new("robin-example");
    public static readonly ViewerId Avery = new("avery-example");
    public static readonly ViewerId Pip = new("pip-example");

    public static readonly TitleId Lantern = new("lantern-film-example");
    public static readonly TitleId Orchard = new("orchard-show-example");
    public static readonly TitleId Kestrel = new("kestrel-film-example");

    public static readonly ServiceId Streamly = new("streamly-example");
    public static readonly ServiceId Flicker = new("flicker-example");

    public static readonly DateTimeOffset Evening = new(2026, 4, 3, 20, 15, 30, TimeSpan.FromHours(-5));

    private static long wall = 1_000;

    /// <summary>A fresh, strictly increasing stamp, so every edit in a test outranks the last.</summary>
    public static HlcTimestamp Next() => new(Interlocked.Add(ref wall, 10), 0);
}
