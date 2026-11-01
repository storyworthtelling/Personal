namespace Foreword.Infrastructure.Prayer;

/// <summary>
/// The log categories that may never carry what the owner writes on the prayer page (A2, LOG-LOOP-PLAN.md §4.6).
/// The head's file logger keeps only a template, a level, an exception's type and its stack for anything under
/// these: the prayer pipeline's namespace, the printed prayer pages, Domain's prayer types and the phone's own
/// rotation editor. <c>PrayerNeverLoggedTests</c> fails if an <c>IPrayerReader</c> is ever outside them.
/// </summary>
public static class PrayerLogCategories
{
    public static IReadOnlyList<string> Quiet { get; } =
    [
        "Foreword.Infrastructure.Prayer",
        "Foreword.Infrastructure.Pdf.PrayerPages",
        "Foreword.Domain.Prayer",
        "Foreword.Android.Pages.RotationEditPage",
    ];
}
