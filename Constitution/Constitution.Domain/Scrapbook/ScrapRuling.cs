namespace Constitution.Domain.Scrapbook;

/// <summary>
/// Where the ruled lines fall, in a scrap's own space (origin at the box's top-left, dp), read off
/// the mockup's gradients. The index card is written ON its rules: each line of text sits on the
/// rule at the foot of its 22 dp row, so a rule is the last dp of a row, not the first. The
/// pitches come from <c>MaterialTokens</c> (the caller passes them), so this holds no second copy.
/// </summary>
public static class ScrapRuling
{
    /// <summary>A rule is one dp thick in both the card and the leaf.</summary>
    public const double RuleThicknessDp = 1;

    /// <summary>The index card's label row (<c>.scrap.index .lab{height:23px}</c>): the head rule is its last dp.</summary>
    public const double IndexLabelRowDp = 23;

    /// <summary>The border (1) plus the card's top padding (9) plus the label row, less the rule's own dp.</summary>
    private const double IndexHeadRule = 1 + 9 + IndexLabelRowDp - RuleThicknessDp;

    /// <summary>The notepad leaf's first rule: <c>repeating-linear-gradient(transparent 0 23px, rule 23px 24px)</c>.</summary>
    private const double NotepadFirstRule = 23;

    /// <summary>The red rule that closes the card's label row.</summary>
    public static double IndexHeadRuleTop() => IndexHeadRule;

    /// <summary>The pale body rules under the head, one per row of <paramref name="pitchDp"/>, none touching the bottom border.</summary>
    public static IReadOnlyList<double> IndexBodyRuleTops(double heightDp, double pitchDp)
    {
        RequirePitch(pitchDp);
        var tops = new List<double>();
        for (var top = IndexHeadRule + pitchDp; top + RuleThicknessDp <= heightDp - 1; top += pitchDp)
        {
            tops.Add(top);
        }

        return tops;
    }

    /// <summary>The notepad leaf's rules, every <paramref name="pitchDp"/> from the torn top.</summary>
    public static IReadOnlyList<double> NotepadRuleTops(double heightDp, double pitchDp)
    {
        RequirePitch(pitchDp);
        var tops = new List<double>();
        for (var top = NotepadFirstRule; top + RuleThicknessDp <= heightDp - 1; top += pitchDp)
        {
            tops.Add(top);
        }

        return tops;
    }

    private static void RequirePitch(double pitchDp)
    {
        if (!(pitchDp > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(pitchDp), pitchDp, "Rules need a positive pitch.");
        }
    }
}
