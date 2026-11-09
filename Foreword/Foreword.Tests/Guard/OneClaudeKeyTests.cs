using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// Foreword reads the shared vault's Claude key, live (audit 01 §3). It used to keep its own "Anthropic" key, under
/// its own credential id, so rotating the key in Folio never reached the handwriting read-back, the task reader or
/// the prayer page. The old local key stands in only while the vault has none.
/// </summary>
public class OneClaudeKeyTests
{
    [Fact]
    public void EveryClaudeOptionReadsTheSharedKeyFirst()
    {
        var program = Read("Foreword", "Foreword.Android", "MauiProgram.cs");

        Assert.DoesNotContain("ApiKey = CredentialStore.AnthropicApiKey", program, StringComparison.Ordinal);
        Assert.Equal(2, Count(program, "ClaudeKeySource.Choose(sp.GetRequiredService<DriveVoiceOptions>().Value.ApiKey, CredentialStore.AnthropicApiKey)"));
    }

    [Fact]
    public void HealthIsReportedUnderTheSharedIdAndThereIsNoSeparateKeyRow()
    {
        var program = Read("Foreword", "Foreword.Android", "MauiProgram.cs");
        var setup = Read("Foreword", "Foreword.Android", "Pages", "SetupPage.xaml.cs");

        Assert.DoesNotContain("CredentialIds.Anthropic", program + setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Claude — handwriting", setup, StringComparison.Ordinal);
    }

    private static int Count(string text, string part) =>
        (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
