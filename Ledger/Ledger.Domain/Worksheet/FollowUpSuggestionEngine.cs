namespace Index.Domain.Worksheet;

/// <summary>One spending category or set-aside line as the follow-up's suggestion engine (Guard G21)
/// sees it: its own key (matching the box that will carry the owner's final figure), display name and
/// optional detail line (mockup 13(b)/(c)'s small "to Redwood Bank &#8211; Bluebird" / "Emergency fund" text),
/// last month's actual, and what was written on the worksheet just read back.</summary>
public sealed record FollowUpCandidateLine(string Key, string Name, string? Detail, long LastMonthWholeDollars, long WrittenWholeDollars);

/// <summary>One line as the follow-up page prints it: last month, what was written, and the suggestion
/// (null when the line is not touched — printed as a dash, D156's "a dash means the line stands as
/// written"). <see cref="IsSuggested"/> lines are the ones the printed page boxes as actionable
/// ("the actionable table"); the rest are reference-only ("Everything else, as written").
///
/// <para><see cref="WasUnreadable"/> marks the third kind of boxed line (D49, R-G25): a limit the worksheet
/// reader could not make out, sent back with a box of its own for the figure to be written again. It has
/// no suggestion — there is nothing sensible to suggest for a figure nobody has read — and its
/// <see cref="WrittenWholeDollars"/> is only whatever is stored, so the page prints "not read" in that
/// column instead of the figure, and the read-back never falls back to it.</para></summary>
public sealed record FollowUpSuggestion(
    string Key, string Name, string? Detail, long LastMonthWholeDollars, long WrittenWholeDollars, long? SuggestedWholeDollars,
    bool WasUnreadable = false)
{
    public bool IsSuggested => SuggestedWholeDollars is not null;

    /// <summary>Whether this is a savings set-aside line (its key says so).</summary>
    public bool IsSavingsLine => Key.StartsWith(WorksheetRegions.SetAsideSavingsPrefix, StringComparison.Ordinal);

    /// <summary>How the line is named in the "Everything else, as written" list. A savings line is named
    /// for its account or goal ("Emergency fund", "Summer trip" — mockup 13(b)); printing its title would
    /// give two rows both called "Savings".</summary>
    public string ReferenceLabel => IsSavingsLine && !string.IsNullOrWhiteSpace(Detail) ? Detail : Name;

    /// <summary>The line as the actionable table prints it, title and detail together — what the
    /// read-back is told the row says, so it can tell two savings rows apart.</summary>
    public string PrintedName => string.IsNullOrWhiteSpace(Detail) ? Name : $"{Name} ({Detail})";
}

/// <summary>
/// D153/D156, Guard G21: the follow-up's own suggestions.
///
/// <para><b>Over</b> (mockup 13(b)): trim the lines furthest above last month's spending, largest rise
/// first, never below last month's own figure, until the excess is gone — the last line trimmed takes
/// only what's left. Set-aside lines never enter this call at all (D156's "set-aside lines are never
/// trimmed" is structural: <see cref="SuggestOver"/> only ever sees the candidates its caller hands it,
/// and the worksheet composer must never hand it a set-aside line).</para>
///
/// <para><b>Under</b> (mockup 13(c)): the whole remainder goes to the extra debt payment line — the
/// savings lines are returned untouched (<c>SuggestedWholeDollars: null</c>), shown with their own boxes
/// so the owner can split the remainder differently instead.</para>
/// </summary>
public static class FollowUpSuggestionEngine
{
    public static IReadOnlyList<FollowUpSuggestion> SuggestOver(
        IReadOnlyList<FollowUpCandidateLine> trimmableLines, long excessWholeDollars)
    {
        // Only a line that actually rose is a candidate — a flat or falling line has nothing to give
        // back. Largest rise first (D156); a tie keeps the caller's own printed order (OrderByDescending
        // is stable), which is as good a tiebreak as any since the mockup's own example has none.
        var candidatesInTrimOrder = trimmableLines
            .Select(line => (Line: line, Rise: line.WrittenWholeDollars - line.LastMonthWholeDollars))
            .Where(x => x.Rise > 0)
            .OrderByDescending(x => x.Rise)
            .ToList();

        var remaining = excessWholeDollars;
        var suggestedByKey = new Dictionary<string, long>();
        foreach (var (line, rise) in candidatesInTrimOrder)
        {
            if (remaining <= 0)
            {
                break;
            }

            // Never below last month's own figure: the trim taken from this line is capped at its own
            // rise, so it can drop back to last month at most — and the LAST line trimmed takes only
            // what's left of the excess, exactly mockup 13(b)'s Gifts ($150 written, $60 last month,
            // trimmed only to $65 — not all the way back to $60).
            var trim = Math.Min(rise, remaining);
            suggestedByKey[line.Key] = line.WrittenWholeDollars - trim;
            remaining -= trim;
        }

        return trimmableLines
            .Select(line => new FollowUpSuggestion(
                line.Key, line.Name, line.Detail, line.LastMonthWholeDollars, line.WrittenWholeDollars,
                suggestedByKey.TryGetValue(line.Key, out var suggested) ? suggested : null))
            .ToList();
    }

    /// <summary>
    /// The over case's rows in the order the suggestions were taken: largest rise first (the lines
    /// furthest above last month's), the order the page lists them in — mockup 13(b) reads Clothing,
    /// Groceries, Gifts, not the order the worksheet printed the categories in. A tie keeps the order it
    /// arrived in. Only the order changes; no figure does.
    /// </summary>
    public static IReadOnlyList<FollowUpSuggestion> InTrimOrder(IEnumerable<FollowUpSuggestion> suggestions) =>
        suggestions
            .OrderByDescending(s => s.WrittenWholeDollars - s.LastMonthWholeDollars)
            .ToList();

    public static (FollowUpSuggestion ExtraPayment, IReadOnlyList<FollowUpSuggestion> Savings) SuggestUnder(
        FollowUpCandidateLine extraPayment, IReadOnlyList<FollowUpCandidateLine> savingsLines, long remainderWholeDollars)
    {
        var extraSuggestion = new FollowUpSuggestion(
            extraPayment.Key, extraPayment.Name, extraPayment.Detail,
            extraPayment.LastMonthWholeDollars, extraPayment.WrittenWholeDollars,
            extraPayment.WrittenWholeDollars + remainderWholeDollars);

        // Untouched, shown with their own boxes (the owner can still choose to redirect some of the
        // remainder there instead) — D156's own wording, mockup 13(c)'s dash column.
        var savingsSuggestions = savingsLines
            .Select(line => new FollowUpSuggestion(
                line.Key, line.Name, line.Detail, line.LastMonthWholeDollars, line.WrittenWholeDollars, null))
            .ToList();

        return (extraSuggestion, savingsSuggestions);
    }
}
