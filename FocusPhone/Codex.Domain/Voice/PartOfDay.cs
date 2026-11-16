namespace Codex.Domain.Voice;

/// <summary>
/// The part of the day a greeting is told (efficiency audit §4): one of its facts, so a line written in the morning is
/// written again in the afternoon rather than shown all day.
/// </summary>
public static class PartOfDay
{
    public static string Of(DateTime local) => local.Hour switch
    {
        >= 5 and < 12 => "morning",
        >= 12 and < 17 => "afternoon",
        _ => "evening",
    };
}
