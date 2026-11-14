using System.Security.Cryptography;
using System.Text;

namespace Codex.Domain.Voice;

/// <summary>
/// Which greeting a stored line answers (efficiency audit §4): the occasion, the part of the day, the day and a hash
/// of what the line was asked about. A key that matches is reused instead of composing again on the strong model,
/// which every cold start of Flyleaf and every open of Score used to do. The context is kept only as a hash, since it
/// can describe the owner's day.
/// </summary>
public static class GreetingCache
{
    public static string PartOfDay(DateTime local) => local.Hour switch
    {
        >= 5 and < 12 => "morning",
        >= 12 and < 17 => "afternoon",
        _ => "evening",
    };

    public static string Key(string purpose, string context, DateTime local)
    {
        // An evening that runs past midnight is the evening of the day it began.
        var day = DateOnly.FromDateTime(local.Hour < 5 ? local.AddDays(-1) : local);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)))[..16];
        return $"{purpose}|{day:yyyy-MM-dd}|{PartOfDay(local)}|{hash}";
    }
}
