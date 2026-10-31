using System.Text.RegularExpressions;
using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Storyworthtelling.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// The word list behind <c>NoFaithContent</c>: Constitution's voice says nothing religious, anywhere
/// (brief, Design rules). Whole words only, so "lentil" is not "lent". The list is a net, not a
/// definition: it exists so that a faith word reaching a prompt or a fallback fails a test instead of
/// reaching the owner's page.
/// </summary>
internal static class FaithWords
{
    private static readonly Regex Pattern = new(
        @"\b(sabbath|shabbat|church\w*|chapel|pray\w*|god|gods|lord|jesus|christ\w*|bible|biblical|scripture\w*|worship\w*|"
        + @"faith\w*|holy|hymn\w*|psalm\w*|gospel|sermon\w*|devotion\w*|amen|bless\w*|temple|mosque|synagogue|rosary|"
        + @"communion|lent|advent|easter|passover|ramadan|hanukkah|sacred|spiritual\w*|religio\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Find(string text) =>
        Pattern.Matches(text).Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// A sweep of invented inputs through every facts builder, then through the real
/// <see cref="SeatComposer"/> with a recording client, so what is scanned is what a model would be
/// sent (purpose and context) plus the facts and the plain fallback shown without one.
/// </summary>
internal static class SeatSweep
{
    private static readonly string[] Dishes =
        ["lentil soup", "roast chicken", "sheet-pan salmon", "mushroom risotto", "bean chili", "lemon pasta"];

    public static IReadOnlyList<SeatRequest> Requests(string? extraDish = null)
    {
        var dishes = extraDish is null ? Dishes : [.. Dishes, extraDish];
        var requests = new List<SeatRequest>();

        var dinners = dishes.Select(DinnerPlan.At).Append(DinnerPlan.Out).ToList();
        foreach (var dinner in dinners)
        {
            requests.Add(SeatFacts.RestDay(new RestDayFacts(dinner)));
            foreach (var kind in new[] { "strength", "intervals", "gentle walk", "mobility" })
            {
                foreach (var hour in new[] { 0, 5, 12, 17, 23 })
                {
                    requests.Add(SeatFacts.Today(new TodayFacts(new TimeOnly(hour, 30), kind, 40, dinner)));
                }
            }
        }

        foreach (var unit in Enum.GetValues<SignalUnit>())
        {
            foreach (var risingIsWorse in new[] { true, false })
            {
                foreach (var (latest, past) in new[] { (6.2, 6.87), (7.5, 6.87), (6.87, 6.87) })
                {
                    requests.Add(SeatFacts.Signal(new SignalFacts("Sleep", unit, latest, "last night", past, "October", risingIsWorse)));
                }

                requests.Add(SeatFacts.BodyMonthly(new BodyMonthFacts(
                [
                    new SignalMonth("Sleep", unit, 6.2, 6.87, risingIsWorse),
                    new SignalMonth("Weight", SignalUnit.Pounds, 184, 181, RisingIsWorse: true),
                    new SignalMonth("Steps", SignalUnit.Steps, 8000, 8100, RisingIsWorse: false),
                ])));
            }
        }

        foreach (var device in new[] { "watch", "scale", "bed" })
        {
            requests.Add(SeatFacts.BodyMonthly(new BodyMonthFacts(
                [new SignalMonth("Sleep", SignalUnit.Hours, 6.2, 6.87, false)], device)));
        }

        foreach (var (today, last) in new[] { (25.0, 20.0), (27.5, 25.0), (135.0, 130.0) })
        {
            requests.Add(SeatFacts.WorkoutEncouragement(new ProgressionFacts("goblet squat", today, last))!);
        }

        var week = Enum.GetValues<DayOfWeek>();
        foreach (var weekOf in new[] { new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 20), new DateOnly(2027, 4, 4) })
        {
            requests.Add(SeatFacts.MealSheetIntro(new MealWeekFacts(weekOf, [], [])));
            foreach (var day in week)
            {
                requests.Add(SeatFacts.MealSheetIntro(new MealWeekFacts(weekOf, [day], [week[(((int)day) + 1) % 7]])));
            }

            requests.Add(SeatFacts.MealSheetIntro(new MealWeekFacts(weekOf, week, week)));
        }

        return requests;
    }

    /// <summary>Every text a swept request puts in front of a model or the owner.</summary>
    public static async Task<IReadOnlyList<string>> TextsAsync(IReadOnlyList<SeatRequest> requests)
    {
        var client = new RecordingVoiceClient();
        var composer = new SeatComposer(
            new VoicedMessenger(new FakeProfileStore(VoiceTestSupport.Profile()), client),
            new DriveVoiceOptions(new FakeConfigStore(new VoiceConfig("not-a-real-key"))));

        var texts = new List<string>();
        foreach (var request in requests)
        {
            await composer.ComposeAsync(request, default);
            texts.AddRange(request.Facts);
            texts.Add(request.Fallback);
        }

        texts.AddRange(client.Requests.Select(sent => sent.Purpose + "\n" + sent.Context));
        texts.AddRange(VoicedOccasions.All.Select(occasion => occasion.Purpose));
        return texts;
    }
}
