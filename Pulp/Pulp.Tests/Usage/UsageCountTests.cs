using System.Reflection;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Usage;
using Pulp.Tests.HandOff;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.Usage;

/// <summary>
/// The usage count Index receives (D90, D269–D271): per service, per calendar month, how many times it
/// was opened through Pulp and — where a session told us — for how many minutes. Shape only: what was
/// watched and by whom never leaves Pulp, so the records carry no title and no viewer.
/// </summary>
public class UsageCountTests
{
    private static readonly ServiceId Flix = HandOffFixtures.Flix.Id;
    private static readonly ServiceId Sample = HandOffFixtures.Sample.Id;
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);

    private static UsageHandOff Opened(ServiceId service, int month, int day, int hour = 20) =>
        new(service, new DateTimeOffset(2026, month, day, hour, 0, 0, Eastern));

    [Fact]
    public void HandOffsAreCountedPerServicePerMonth()
    {
        var rows = UsageCount.Tally(
            [Opened(Flix, 3, 2), Opened(Flix, 3, 20), Opened(Sample, 3, 5), Opened(Flix, 4, 1)],
            []);

        Assert.Equal(
            [
                new ServiceMonthUsage(Flix, new UsageMonth(2026, 3), 2, null, new DateOnly(2026, 3, 20)),
                new ServiceMonthUsage(Sample, new UsageMonth(2026, 3), 1, null, new DateOnly(2026, 3, 5)),
                new ServiceMonthUsage(Flix, new UsageMonth(2026, 4), 1, null, new DateOnly(2026, 4, 1)),
            ],
            rows);
    }

    [Fact]
    public void TheMonthIsTheOneOnThePhonesClockNotUtc()
    {
        // 9pm on 31 March in the phone's offset is already April in UTC; the owner opened it in March.
        var rows = UsageCount.Tally([Opened(Flix, 3, 31, hour: 21)], []);

        Assert.Equal(new UsageMonth(2026, 3), Assert.Single(rows).Month);
    }

    [Fact]
    public void MinutesAreSummedWhereKnownAndAbsentWhereNot()
    {
        var rows = UsageCount.Tally(
            [Opened(Flix, 3, 2), Opened(Sample, 3, 5)],
            [
                new UsageMinutes(Flix, new DateTimeOffset(2026, 3, 2, 21, 0, 0, Eastern), TimeSpan.FromMinutes(42.5)),
                new UsageMinutes(Flix, new DateTimeOffset(2026, 3, 9, 21, 0, 0, Eastern), TimeSpan.FromMinutes(20)),
            ]);

        Assert.Equal(62, rows.Single(row => row.Service == Flix).Minutes);
        Assert.Null(rows.Single(row => row.Service == Sample).Minutes);
    }

    [Fact]
    public void MinutesWithNoHandOffThatMonthStillMakeARow()
    {
        var rows = UsageCount.Tally(
            [],
            [new UsageMinutes(Flix, new DateTimeOffset(2026, 4, 1, 0, 30, 0, Eastern), TimeSpan.FromMinutes(30))]);

        Assert.Equal(new ServiceMonthUsage(Flix, new UsageMonth(2026, 4), 0, 30, null), Assert.Single(rows));
    }

    [Fact]
    public void AStartedPlansHandOffBecomesAUsageEntryWithoutItsTitle()
    {
        var pending = Planner().Plan(Request(HandOffFixtures.Sample, Sam), At).Pending!;

        var entry = UsageHandOff.From(pending);

        Assert.Equal(new UsageHandOff(Sample, At), entry);
    }

    [Fact]
    public void NegativeMinutesAreRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UsageMinutes(Flix, At, TimeSpan.FromMinutes(-1)));

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 5)]
    public void AMonthThatDoesNotExistIsRefused(int year, int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsageMonth(year, month));

    /// <summary>
    /// Shape only (D90): nothing in the usage folder can hold a title, a viewer or free text, so no
    /// later field quietly ships what the household watched to another app.
    /// </summary>
    [Fact]
    public void NothingInTheUsageCountCanCarryATitleAViewerOrText()
    {
        var types = typeof(UsageCount).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(UsageCount).Namespace && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .ToList();
        Assert.True(types.Count >= 4, $"Found only {types.Count} usage types; the guard would pass on nothing.");

        Type[] forbidden = [typeof(TitleId), typeof(ViewerId), typeof(string), typeof(Uri)];
        var offenders = types
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.DeclaringType == type)
                .Where(property => forbidden.Contains(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
                    || property.PropertyType.GetGenericArguments().Any(forbidden.Contains))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, "The usage count gained a field that can carry content: " + string.Join(", ", offenders));
    }
}
