using Constitution.Domain.Calendar;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>
/// The rules planner for the week's dinners (C-6.3.1's rules core, C-6.3.3's rules-only week). Pure: everything it reads is in the
/// <see cref="MealWeekRequest"/>, and the same request is the same week. It plans nothing when the box is short of ten cards or a
/// calendar could not be read (<see cref="Stop"/>); otherwise every evening gets a typed dinner and a reason.
///
/// <para><b>Order of placement.</b> Cook-this-soon cards take the earliest evenings that fit; then one stretch dish (a card not
/// cooked for eight weeks) takes Thursday, else the next evening that fits; then each remaining evening takes the best card by
/// children's table, then variety across cuisines, then least recently cooked, then the seed.</para>
///
/// <para><b>Guesses</b> (the brief is silent, listed in the progress row): a repeat is two dinners fewer than 14 days apart; the
/// block is the card's timed minutes held to 30 to 120 and ends where the family dinner starts; a cook is an adult home at dinner
/// whose block is free; the stretch is a card not cooked in 56 days, on Thursday; leftovers follow a cooked evening only; the
/// rules never plan eating out.</para>
/// </summary>
public static class MealPlanner
{
    /// <summary>A card may not be cooked again sooner than this many days (either side of an evening).</summary>
    public const int RepeatDays = 14;

    /// <summary>A card not cooked in this many days before the week is "not cooked lately" and may be the stretch dish.</summary>
    public const int StretchLookbackDays = 56;

    /// <summary>Thursday, counting from the Monday the week starts on.</summary>
    public const int ThursdayIndex = 3;

    private const string OrdinaryReason = "From the box.";

    public static MealStop? Stop(MealWeekRequest request) => StopOf(request, out _);

    public static MealWeek Plan(MealWeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var stop = StopOf(request, out var sentence);
        return stop is { } reason
            ? MealWeek.NotPlanned(request.WeekStart, reason, sentence)
            : new WeekBuilder(request).Build();
    }

    private static MealStop? StopOf(MealWeekRequest request, out string sentence)
    {
        request.EnsureValid();
        if (request.Recipes.Count < EmptyBox.Target)
        {
            sentence = "The recipe box has " + request.Recipes.Count + " of " + EmptyBox.Target + " cards, so no dinners are planned yet.";
            return MealStop.BoxTooSmall;
        }

        var weekOpens = ZoneClock.StartOfDay(request.WeekStart, request.Rules.Zone);
        var weekCloses = ZoneClock.StartOfDay(request.WeekStart.AddDays(MealWeek.Evenings), request.Rules.Zone);
        foreach (var adult in request.Rules.Adults)
        {
            var time = MealFacts.TimeOf(request, adult.Name);
            if (time is null || !time.IsComplete || time.WindowStart > weekOpens || time.WindowEnd < weekCloses)
            {
                sentence = "A calendar could not be read for the whole week, so no dinners are planned.";
                return MealStop.CalendarUnreadable;
            }
        }

        sentence = string.Empty;
        return null;
    }

    private sealed record Evening(DateOnly Day, bool Passed, DateTimeOffset DinnerStart, DateTimeOffset DinnerEnd, bool ChildrenHome, IReadOnlyList<string> Home);

    private sealed record Fit(string Cook, DateTimeOffset Start, DateTimeOffset End, bool ChildrenDifferently);

    private sealed class WeekBuilder
    {
        private readonly MealWeekRequest request;
        private readonly Evening[] evenings;
        private readonly DinnerChoice?[] choices = new DinnerChoice?[MealWeek.Evenings];
        private readonly Dictionary<RecipeId, Recipe> byId;
        private readonly Dictionary<string, int> cooksSoFar = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> notes = [];

        public WeekBuilder(MealWeekRequest request)
        {
            this.request = request;
            byId = request.Recipes.ToDictionary(recipe => recipe.Id);
            var today = request.Now == DateTimeOffset.MinValue ? DateOnly.MinValue : ZoneClock.DayOf(request.Now, request.Rules.Zone);
            evenings = Enumerable.Range(0, MealWeek.Evenings).Select(offset =>
            {
                var day = request.WeekStart.AddDays(offset);
                var (start, end) = MealFacts.DinnerWindow(request.Rules, day);
                var home = request.Rules.Adults
                    .Where(adult => MealFacts.TimeOf(request, adult.Name) is { } time && MealFacts.IsHome(time, start, end))
                    .Select(adult => adult.Name)
                    .ToList();
                return new Evening(day, day < today, start, end, request.ChildrenHome.Contains(day), home);
            }).ToArray();
        }

        public MealWeek Build()
        {
            PlaceSoon();
            PlaceStretch();
            for (var index = 0; index < choices.Length; index++)
            {
                choices[index] ??= Fill(index);
            }

            return MealWeek.Planned(request.WeekStart, choices.Select(choice => choice!), notes);
        }

        private void PlaceSoon()
        {
            var soon = request.CookThisSoon
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .OrderBy(recipe => MealFacts.LastCooked(recipe.Id, request.History))
                .ThenBy(recipe => recipe.Id.Value, StringComparer.Ordinal);
            foreach (var recipe in soon)
            {
                var placed = false;
                for (var index = 0; index < choices.Length && !placed; index++)
                {
                    placed = TryPlace(index, recipe, stretch: false, "Marked cook this soon.");
                }

                if (!placed)
                {
                    notes.Add(recipe.Title + " was marked cook this soon but does not fit this week.");
                }
            }
        }

        private void PlaceStretch()
        {
            var cutoff = request.WeekStart.AddDays(-StretchLookbackDays);
            var candidates = request.Recipes
                .Where(recipe => !request.CookThisSoon.Contains(recipe.Id) && !choices.Any(choice => choice?.Recipe == recipe.Id))
                .Where(recipe => MealFacts.LastCooked(recipe.Id, request.History) <= cutoff)
                .OrderBy(recipe => MealFacts.LastCooked(recipe.Id, request.History))
                .ThenBy(recipe => MealFacts.Hash(request.Seed, "stretch", recipe.Id.Value))
                .ToList();
            if (candidates.Count == 0)
            {
                notes.Add("No stretch dish this week: every card has been cooked lately.");
                return;
            }

            int[] order = [3, 4, 5, 6, 0, 1, 2];
            foreach (var recipe in candidates)
            {
                foreach (var index in order)
                {
                    if (TryPlace(index, recipe, stretch: true, "This week's stretch dish: a card not cooked lately."))
                    {
                        return;
                    }
                }
            }

            notes.Add("No stretch dish this week: none fits an evening.");
        }

        private DinnerChoice Fill(int index)
        {
            var evening = evenings[index];
            if (evening.Passed)
            {
                return DinnerChoice.NothingPlanned(evening.Day, "That evening has already passed.");
            }

            var best = request.Recipes
                .Select(recipe => (Recipe: recipe, Fit: FitOf(index, recipe)))
                .Where(pair => pair.Fit is not null)
                .OrderBy(pair => pair.Fit!.ChildrenDifferently ? 1 : 0)
                .ThenBy(pair => NextToSameCuisine(index, pair.Recipe) ? 1 : 0)
                .ThenBy(pair => choices.Count(choice => choice?.Recipe is { } id && byId[id].Cuisine == pair.Recipe.Cuisine))
                .ThenBy(pair => MealFacts.LastCooked(pair.Recipe.Id, request.History))
                .ThenBy(pair => MealFacts.Hash(request.Seed, evening.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), pair.Recipe.Id.Value))
                .FirstOrDefault();
            if (best.Recipe is not null)
            {
                return Place(index, best.Recipe, best.Fit!, stretch: false, OrdinaryReason);
            }

            if (evening.Home.Count == 0)
            {
                return DinnerChoice.NothingPlanned(evening.Day, "Nobody is home that evening.");
            }

            if (index > 0 && choices[index - 1]?.Kind == DinnerKind.Cooked)
            {
                return DinnerChoice.Leftovers(evening.Day, "Leftovers from the evening before.");
            }

            return DinnerChoice.NothingPlanned(
                evening.Day,
                AnyoneCanCook(index) ? "Every card was cooked in the last two weeks or is one someone has said never again to." : "No one is free to cook before dinner.");
        }

        private bool AnyoneCanCook(int index)
        {
            var evening = evenings[index];
            var start = evening.DinnerStart - MealFacts.ShortestCook;
            return evening.Home.Any(name => MealFacts.TimeOf(request, name)!.IsFree(start, evening.DinnerStart));
        }

        private bool NextToSameCuisine(int index, Recipe recipe) =>
            new[] { index - 1, index + 1 }.Any(other => other >= 0 && other < choices.Length && choices[other]?.Recipe is { } id && byId[id].Cuisine == recipe.Cuisine);

        private bool TryPlace(int index, Recipe recipe, bool stretch, string reason)
        {
            var fit = FitOf(index, recipe);
            if (fit is null)
            {
                return false;
            }

            choices[index] = Place(index, recipe, fit, stretch, reason);
            return true;
        }

        private DinnerChoice Place(int index, Recipe recipe, Fit fit, bool stretch, string reason)
        {
            cooksSoFar[fit.Cook] = cooksSoFar.GetValueOrDefault(fit.Cook) + 1;
            var said = fit.ChildrenDifferently ? reason + " The children get a different meal." : reason;
            return DinnerChoice.Cooked(evenings[index].Day, recipe.Id, fit.Cook, fit.Start, fit.End, stretch, fit.ChildrenDifferently, said);
        }

        private Fit? FitOf(int index, Recipe recipe)
        {
            var evening = evenings[index];
            if (evening.Passed || choices[index] is not null || evening.Home.Count == 0)
            {
                return null;
            }

            var others = choices.Where(choice => choice?.Recipe is not null).Select(choice => new CookedMeal(choice!.Recipe!.Value, choice.Day));
            if (MealFacts.RepeatsWithin(recipe.Id, evening.Day, request.History.Concat(others)))
            {
                return null;
            }

            var different = evening.ChildrenHome && !recipe.ForKids;
            if (request.NeverAgain.TryGetValue(recipe.Id, out var people))
            {
                foreach (var name in people)
                {
                    var adult = request.Rules.FindAdult(name);
                    if (adult is not null)
                    {
                        if (evening.Home.Contains(adult.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            return null;
                        }
                    }
                    else if (evening.ChildrenHome)
                    {
                        different = true;
                    }
                }
            }

            var end = evening.DinnerStart;
            var start = end - MealFacts.CookLength(recipe);
            if (start < request.Now)
            {
                return null;
            }

            var cook = evening.Home
                .Where(name => MealFacts.TimeOf(request, name)!.IsFree(start, end))
                .OrderBy(name => cooksSoFar.GetValueOrDefault(name))
                .ThenBy(name => MealFacts.Hash(request.Seed, evening.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), name))
                .FirstOrDefault();
            return cook is null ? null : new Fit(cook, start, end, different);
        }
    }
}
