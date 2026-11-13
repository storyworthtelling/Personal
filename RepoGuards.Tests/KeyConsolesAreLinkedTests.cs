namespace RepoGuards.Tests;

/// <summary>
/// Every settings screen that asks for a key made in a provider's developer console offers a link to that
/// console, through the one table (<c>Storyworthtelling.Voice.Health.KeyConsoles</c>) rather than an address
/// typed into the screen. Each row names the screen and the credential it must link; a new key field joins
/// the table, and an address written into a screen fails <see cref="NoScreenTypesAConsoleAddress"/>.
/// </summary>
public class KeyConsolesAreLinkedTests
{
    public static readonly TheoryData<string, string> Screens = new()
    {
        { "Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs", "CredentialIds.Claude" },
        { "Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs", "CredentialIds.Gemini" },
        { "Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs", "KeyConsoles.GoogleOAuthClient" },
        { "Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs", "RemarkableCredentialId.Value" },
        { "Ledger/Ledger.Android/Pages/SetupPage.xaml.cs", "CredentialIds.Plaid" },
        { "Foreword/Foreword.Android/Pages/SetupPage.xaml.cs", "CredentialIds.Maps" },
        { "Foreword/Foreword.Android/Pages/SetupPage.xaml.cs", "RemarkableCredentialId.Value" },
        { "Foreword/Foreword.Android/Pages/SetupPage.xaml.cs", "KeyConsoleLink.For(id)" },
        { "Pulp/Pulp.Android/Vault/VaultActivity.cs", "KeyConsoleLink.For(this, key.CredentialId)" },
        { "FocusPhone/Post.Android/SettingsArea.cs", "CredentialIds.GmailAppPassword" },
        { "FocusPhone/Flyleaf.Android/UnlockActivity.cs", "CredentialIds.GitHubUpdateToken" },
        { "FocusPhone/Score.Android/TidalPairingActivity.cs", "CredentialIds.Tidal" },
        { "FocusPhone/Plate.Android/SettingsArea.cs", "SharedCredentialIds.Remarkable" },
        { "Constitution/Constitution.Android/SettingsArea.cs", "SharedCredentialIds.Remarkable" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheScreenLinksTheConsole(string screen, string credential)
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), screen));
        var links = text.Split('\n')
            .Where(line => line.Contains("KeyConsole", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            links.Any(line => line.Contains(credential, StringComparison.Ordinal)),
            $"{screen} asks for a key but links no console for {credential}.");
    }

    [Fact]
    public void NoScreenTypesAConsoleAddress()
    {
        string[] addresses =
        [
            "console.anthropic.com", "platform.claude.com", "aistudio.google.com", "console.cloud.google.com",
            "developer.nytimes.com", "dashboard.plaid.com", "omdbapi.com/apikey", "github.com/settings",
            "myaccount.google.com/apppasswords", "developer.tidal.com", "developers.movieofthenight.com",
        ];
        var root = GuardScan.RepoRoot();
        var screens = Screens.Select(row => (string)row[0]).Distinct()
            .SelectMany(screen => new[] { screen, screen.EndsWith(".xaml.cs", StringComparison.Ordinal) ? screen[..^3] : screen })
            .Distinct()
            .Where(screen => File.Exists(Path.Combine(root, screen)));

        var typed = screens
            .SelectMany(screen => File.ReadAllLines(Path.Combine(root, screen))
                .Select((line, index) => (screen, line, index))
                .Where(row => !row.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Where(row => addresses.Any(address => row.line.Contains(address, StringComparison.OrdinalIgnoreCase)))
                .Select(row => $"{row.screen}:{row.index + 1}"))
            .ToList();

        Assert.True(typed.Count == 0, "Console addresses typed into a screen: " + string.Join(", ", typed));
    }
}
