using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Pdf;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// Writes a realistic prayer document to disk when FOREWORD_PRAYER_SAMPLE names a path, so the two
/// pages can be looked at rather than only asserted about. Skipped otherwise — it is a tool, not a
/// check.
/// </summary>
public class SamplePrayerTests
{
    [Fact]
    public void WriteSample()
    {
        var path = Environment.GetEnvironmentVariable("FOREWORD_PRAYER_SAMPLE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var day = new DateOnly(2026, 9, 13);

        var document = new PrayerDocument
        {
            Date = day,
            Lead = PrayerText.Of("A short list this morning, and one of them can come off it."),
            Holding =
            [
                new PrintedRequest(
                    PrayerText.Of("Dad — the scan on the 14th"), "held 9 days", false),
                new PrintedRequest(
                    PrayerText.Of("The Kellers, moving"), "held 3 weeks", false),
                new PrintedRequest(
                    PrayerText.Of("Riley, settling into the new class"), "held 6 weeks", false),
                new PrintedRequest(
                    PrayerText.Of("Sam's Boston trip"), "held 4 days", false),
                new PrintedRequest(
                    PrayerText.Of("Work — the decision about the team"), "answered", true),
            ],
            Rotations =
            [
                new RotationPicked("Missionaries", PrayerText.Of("the Boyd family, Nairobi"), "2 of 9"),
            ],
            FromBefore =
            [
                new SurfacedEntry("Yesterday", PrayerText.Of(
                    "Asked for patience with the mornings, and got a week of them.")),
                new SurfacedEntry("A week ago", PrayerText.Of(
                    "Still turning over whether to say something to Dan. Not yet.")),
                new SurfacedEntry("This date, 2025", PrayerText.Of(
                    "First day of the new job. Asked not to be an idiot about it.")),
            ],
        };

        File.WriteAllBytes(path, new PrayerPages().Compose(document));
    }
}
