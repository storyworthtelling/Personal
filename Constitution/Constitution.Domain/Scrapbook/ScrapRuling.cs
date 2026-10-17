using Constitution.Domain.Scraps;

namespace Constitution.Domain.Scrapbook;

/// <summary>Where the ruled lines of the index card and the notepad leaf fall (stub).</summary>
public static class ScrapRuling
{
    public const double RuleThicknessDp = 1;

    public static double IndexHeadRuleTop() => throw new NotImplementedException();

    public static IReadOnlyList<double> IndexBodyRuleTops(double heightDp, double pitchDp) =>
        throw new NotImplementedException();

    public static IReadOnlyList<double> NotepadRuleTops(double heightDp, double pitchDp) =>
        throw new NotImplementedException();
}
