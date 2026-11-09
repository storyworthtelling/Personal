namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// Options read at the moment they are asked for (audit 01 §18): three MAUI heads each kept a private copy, and
/// Constitution two more as LiveKey, while two of its own call sites took a snapshot of the key instead, so a key
/// entered after launch never reached them.
/// </summary>
public class LiveOptionsTests
{
    [Fact]
    public void EachReadSeesTheValueAsItIsNow()
    {
        var key = "first";
        var options = new LiveOptions<VoiceOptions>(() => new VoiceOptions { ApiKey = key });

        Assert.Equal("first", options.Value.ApiKey);
        key = "second";
        Assert.Equal("second", options.Value.ApiKey);
    }
}
