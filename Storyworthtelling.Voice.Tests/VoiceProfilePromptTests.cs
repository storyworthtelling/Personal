using System.Text.Json;
using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// The one way a prompt carries the owner's voice (audit W10.2). The compose call, Plate's caption and
/// Foreword's prayer line all send the same block, so a seat outside the messenger cannot drift into
/// guessing at the voice ("dry, warm") rather than reading it.
/// </summary>
public class VoiceProfilePromptTests
{
    private static readonly VoiceProfile Learned = VoiceProfile.Empty with
    {
        Register = "wry, warm, economical",
        Traits = ["plain-spoken"],
        Examples = [new VoiceExample("At last, the gate.", Guid.NewGuid())],
        Avoid = ["hype"],
        Cadence = ["short declarative openers"],
        Punctuation = ["no exclamation marks"],
        DocumentsAnalyzed = 2,
    };

    [Fact]
    public void TheBlockNamesItselfAndCarriesEveryPartOfTheVoice()
    {
        var block = VoiceProfilePrompt.Block(Learned);

        Assert.StartsWith("PROFILE (JSON):\n", block);
        using var json = JsonDocument.Parse(block["PROFILE (JSON):\n".Length..]);
        var root = json.RootElement;
        Assert.Equal("wry, warm, economical", root.GetProperty("register").GetString());
        Assert.Equal("plain-spoken", root.GetProperty("traits")[0].GetString());
        Assert.Equal("At last, the gate.", root.GetProperty("examples")[0].GetString());
        Assert.Equal("hype", root.GetProperty("avoid")[0].GetString());
        Assert.Equal("short declarative openers", root.GetProperty("cadence")[0].GetString());
        Assert.Equal("no exclamation marks", root.GetProperty("punctuation")[0].GetString());
    }

    [Fact]
    public void TheBlockNeverCarriesWhichBookAnExampleCameFrom()
    {
        var id = Learned.Examples[0].ProjectId;

        Assert.DoesNotContain(id.ToString(), VoiceProfilePrompt.Block(Learned));
    }

    [Fact]
    public void AReadOnlySourceIsEnoughToReadTheVoice()
    {
        // A seat that only speaks takes the read half; nothing it is handed can write the profile.
        Assert.True(typeof(IVoiceProfileSource).IsAssignableFrom(typeof(IVoiceProfileStore)));
        Assert.Equal(["LoadAsync"], typeof(IVoiceProfileSource).GetMethods().Select(method => method.Name));
    }
}
