using Constitution.Domain.Settings;
using Constitution.Tests.FirstRun;

namespace Constitution.Tests.Settings;

/// <summary>
/// Backing out of the Google account picker is the person's answer, not a failure: it was logged as a failed key
/// action and as a WARN sign-in failure on the vault trail. The shared sign-in now throws its own
/// <c>GoogleAccountPickerCancelledException</c> (an <see cref="InvalidOperationException"/>, so every other head's
/// catch still holds), and both of Constitution's sign-ins catch it first: the action settles as answered, the row
/// says no account was chosen, and nothing goes on the vault trail. The heads are compile-checked, not run here.
/// </summary>
public class PickerCancelSourceTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([ConstitutionSource.Root(), .. parts]));

    private static string PickerCancelCatch(string signIn)
    {
        var at = signIn.IndexOf("catch (GoogleAccountPickerCancelledException)", StringComparison.Ordinal);
        Assert.True(at >= 0, "The sign-in doesn’t catch the picker being cancelled.");
        Assert.True(at < signIn.IndexOf("ConnectionTrail.Expecting", StringComparison.Ordinal),
            "The cancel has to be caught before the failure filter, which records it on the vault trail.");
        return FirstRunTabsSourceTests.Body(signIn[at..], "catch (GoogleAccountPickerCancelledException)");
    }

    [Theory]
    [InlineData("Constitution.Android/FirstRun/FirstRunPage.cs")]
    [InlineData("Constitution.Android/SettingsArea.cs")]
    public void ACancelledPickerIsAnAnswerNotAFailure(string file)
    {
        var signIn = FirstRunTabsSourceTests.Body(Read(file.Split('/')), "private async Task SignInAsync()");
        var cancel = PickerCancelCatch(signIn);

        Assert.Contains("signing.Succeeded()", cancel, StringComparison.Ordinal);
        Assert.Contains("ConnectionFailure.NoAccountChosen", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed(", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionTrail", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoteIsPlain()
    {
        Assert.Equal("No account was chosen.", ConnectionFailure.NoAccountChosen);
    }

    [Fact]
    public void BothCancelPathsOfThePickerThrowTheirOwnType()
    {
        var kit = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "..", "FocusPhone", "Codex.Ui.Android", "Voice", "VoiceGoogleAccount.cs"));
        var handler = FirstRunTabsSourceTests.Body(kit, "public static void HandleChooserResult(");

        Assert.Equal(2, handler.Split("new GoogleAccountPickerCancelledException(").Length - 1);
        Assert.DoesNotContain("new InvalidOperationException(", handler, StringComparison.Ordinal);
        Assert.Contains("class GoogleAccountPickerCancelledException : InvalidOperationException", kit, StringComparison.Ordinal);
    }
}
