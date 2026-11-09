using Storyworthtelling.Voice.Claude;

namespace Storyworthtelling.Voice.Tests.Claude;

/// <summary>
/// The Claude key is the shared vault's, read live (audit 01 §3). Foreword kept a separate key and Folio and
/// Constitution copied the shared one into local settings once, so a key rotated in the vault never reached them.
/// An app that still holds an old local key uses it only while the vault has none, so no phone stops working on
/// the update.
/// </summary>
public class ClaudeKeySourceTests
{
    [Theory]
    [InlineData("shared", "old", "shared")]
    [InlineData("shared", "", "shared")]
    [InlineData("", "old", "old")]
    [InlineData(" ", "old", "old")]
    [InlineData(null, null, "")]
    public void TheSharedKeyWinsAndALocalOneOnlyStandsIn(string? shared, string? local, string expected)
    {
        Assert.Equal(expected, ClaudeKeySource.Choose(shared, local));
    }

    [Fact]
    public void AKeyIsTrimmed() => Assert.Equal("shared", ClaudeKeySource.Choose("  shared\n", null));
}
