namespace Constitution.Domain.Documents;

/// <summary>One row of the clock: a step and the time it is due, or the dinner itself when <see cref="Step"/> is null.</summary>
public sealed record ClockedRow(DateTime At, RecipeClockStep? Step)
{
    public bool IsDinner => Step is null;
}

/// <summary>
/// The steps on the clock. <see cref="SqueezedTo"/> is the earliest start the cook was given when the steps had to be
/// pulled forward to it, so that none starts before the page was handed over; null when nothing had to move.
/// </summary>
public sealed record RecipeClock(IReadOnlyList<ClockedRow> Rows, DateTime? SqueezedTo);

/// <summary>
/// Puts a card's steps on tonight's clock. Evening steps are counted back from dinner, each ending when the next begins,
/// so the cook block ends exactly at dinner; a slow-cooker meal's morning steps run forward from the morning start and
/// come first. Times are wall-clock arithmetic on the day's own face: a day the clocks change reads the same in the
/// evening, because no cook block spans the small hours.
/// </summary>
public static class RecipeSchedule
{
    public static RecipeClock Plan(IReadOnlyList<RecipeClockStep> steps, DateTime dinnerAt, DateTime morningStartAt, DateTime? notBefore = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Validate(steps);

        var rows = new List<ClockedRow>();
        var squeezed = false;

        var morning = steps.Where(s => s.Morning).ToList();
        if (morning.Count > 0)
        {
            var cursor = morningStartAt;
            if (notBefore is { } now && now > cursor)
            {
                cursor = now;
                squeezed = true;
            }

            foreach (var step in morning)
            {
                rows.Add(new ClockedRow(cursor, step));
                cursor = cursor.AddMinutes(step.Minutes);
            }
        }

        var evening = steps.Where(s => !s.Morning).ToList();
        var starts = new DateTime[evening.Count];
        var at = dinnerAt;
        for (var i = evening.Count - 1; i >= 0; i--)
        {
            at = at.AddMinutes(-evening[i].Minutes);
            starts[i] = at;
        }

        for (var i = 0; i < evening.Count; i++)
        {
            var start = starts[i];
            if (notBefore is { } now && start < now)
            {
                start = now;
                squeezed = true;
            }

            rows.Add(new ClockedRow(start, evening[i]));
        }

        rows.Add(new ClockedRow(dinnerAt, null));
        return new RecipeClock(rows, squeezed ? notBefore : null);
    }

    /// <summary>Every step's minutes added up.</summary>
    public static int TotalMinutes(IReadOnlyList<RecipeClockStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Sum(s => s.Minutes);
    }

    /// <summary>The minutes the cook is busy for: the steps marked active.</summary>
    public static int HandsOnMinutes(IReadOnlyList<RecipeClockStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Where(s => s.Active).Sum(s => s.Minutes);
    }

    private static void Validate(IReadOnlyList<RecipeClockStep> steps)
    {
        if (steps.Count == 0)
        {
            throw new ArgumentException("A recipe with no steps has nothing to put on the clock.", nameof(steps));
        }

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Text))
            {
                throw new ArgumentException("A step needs words to print.", nameof(steps));
            }

            if (step.Minutes < 1)
            {
                throw new ArgumentException($"The step '{step.Text}' takes {step.Minutes} minutes; a step on the clock takes at least one.", nameof(steps));
            }
        }
    }
}
