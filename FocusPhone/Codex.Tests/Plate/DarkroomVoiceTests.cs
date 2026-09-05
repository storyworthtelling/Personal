using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The owner's voice gets one seat in Plate, exactly as it gets one in Post: the empty
/// Darkroom. Set in Pinyon Script in ink-blue, the one place in the whole suite that hand
/// appears.
/// </summary>
public class DarkroomVoiceTests
{
    [Fact]
    public void AnEmptyRoomIsNeverTheSameLineTwiceRunning()
    {
        // A line that repeats every time stops being a voice and becomes a label. Post's
        // empty Mailbox draws from a pool for the same reason.
        var first = DarkroomVoice.Empty(0);
        var second = DarkroomVoice.Empty(1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ThePoolIsBigEnoughToNotFeelLikeAPool()
    {
        Assert.True(DarkroomVoice.EmptyLines.Count >= 5,
            "Fewer than five lines and the owner learns them all in a week.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    [InlineData(-3)]
    public void AnyRollLandsOnARealLine(int roll)
    {
        // The roll comes from Random.Shared.Next(), which can be anything including
        // negative on some paths. An index out of range here would be a crash on the one
        // screen that exists to be pleasant.
        Assert.Contains(DarkroomVoice.Empty(roll), DarkroomVoice.EmptyLines);
    }

    [Fact]
    public void NoLineNagsAboutWhatIsWaiting()
    {
        // The empty room has nothing waiting by definition, and the owner refused every
        // form of nagging about the room's contents. A line that scolds would be the nag
        // arriving through the back door.
        foreach (var line in DarkroomVoice.EmptyLines)
        {
            Assert.DoesNotContain("should", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("forgot", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("still", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EveryLineIsShortEnoughToBeSetInAHand()
    {
        // Pinyon Script at display size across a phone. Past roughly this length it wraps to
        // three lines and reads as a paragraph in a calligraphy face, which is unreadable.
        Assert.All(DarkroomVoice.EmptyLines, line => Assert.True(line.Length <= 60, line));
    }

    [Fact]
    public void TheCaptionPromptAsksForOneLineAboutWhatIsThere()
    {
        // The captions are written from what the vision call saw. The prompt is here rather
        // than in the head so its shape can be read without a phone or an API key.
        var prompt = DarkroomVoice.CaptionPrompt(4);

        Assert.Contains("4", prompt, StringComparison.Ordinal);
        Assert.Contains("one", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASinglePictureIsNotAskedAboutInThePlural()
    {
        Assert.DoesNotContain("pictures", DarkroomVoice.CaptionPrompt(1), StringComparison.Ordinal);
    }
}
