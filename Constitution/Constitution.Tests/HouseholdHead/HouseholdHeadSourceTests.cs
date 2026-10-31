using System.Text.RegularExpressions;

namespace Constitution.Tests.HouseholdHead;

/// <summary>Guards over the files that wire the household file into the head: what they may name, and what they may never.</summary>
public class HouseholdHeadSourceTests
{
    private static IReadOnlyList<(string Relative, string Text)> Wiring() =>
        ConstitutionSource.Files(".cs")
            .Select(file => (Relative: ConstitutionSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Relative.StartsWith("Constitution.Android/Household/", StringComparison.Ordinal)
                || file.Relative.StartsWith("Constitution.Infrastructure/Household/", StringComparison.Ordinal))
            .ToList();

    // Assembled so this file is not itself what the repository-wide spinner guard finds.
    private const string Spinner = "Progress" + "Bar|" + "Progress" + "Dialog";

    private static string Source(string relative) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file) == relative));

    [Fact]
    public void TheGuardReadsTheRealFiles()
    {
        var names = Wiring().Select(file => file.Relative).ToList();

        Assert.Contains("Constitution.Android/Household/ConstitutionHousehold.cs", names);
        Assert.Contains("Constitution.Infrastructure/Household/HouseholdFileService.cs", names);
        Assert.True(names.Count >= 8, "Only " + names.Count + " wiring files were found.");
    }

    [Fact]
    public void NoColourOrTypeSizeLiteral()
    {
        foreach (var (relative, text) in Wiring())
        {
            Assert.False(Regex.IsMatch(text, "#[0-9A-Fa-f]{6,8}\\b|Color\\.Argb|ComplexUnitType|SetTextSize", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative);
        }
    }

    [Fact]
    public void NoEmptyCatchAndNoBareCatch()
    {
        foreach (var (relative, text) in Wiring())
        {
            Assert.False(Regex.IsMatch(text, @"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative + " has an empty catch.");
            Assert.False(Regex.IsMatch(text, @"catch\s*\{", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative + " has a catch with no type.");
        }
    }

    [Fact]
    public void OnlyThePrivateNoBackupFolderIsUsed()
    {
        foreach (var (relative, text) in Wiring())
        {
            Assert.False(Regex.IsMatch(text, @"(?<!NoBackup)FilesDir|ExternalStorage|ExternalFilesDir|CacheDir|GetExternal", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative);
        }

        Assert.Contains("NoBackupFilesDir", Source("Constitution.Android/Household/ConstitutionHousehold.cs"));
    }

    [Fact]
    public void NoNewPermissionIsDeclared()
    {
        foreach (var (relative, text) in Wiring())
        {
            Assert.DoesNotContain("UsesPermission", text);
            Assert.DoesNotContain("RequestPermissions", text);
        }
    }

    [Fact]
    public void NoUiPromptsOrToasts()
    {
        foreach (var (relative, text) in Wiring())
        {
            Assert.False(Regex.IsMatch(text, "Toast|AlertDialog|" + Spinner, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative);
        }
    }

    [Fact]
    public void NoUnderscoreInAnIdentifier()
    {
        foreach (var (relative, text) in Wiring())
        {
            var code = Regex.Replace(text, "\"(?:[^\"\\\\]|\\\\.)*\"|//[^\n]*", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            Assert.False(Regex.IsMatch(code, @"\b[A-Za-z0-9]+_[A-Za-z0-9_]*\b|\b_[A-Za-z0-9]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative + " has an underscore in an identifier.");
        }
    }

    [Fact]
    public void ThePassphraseIsOnlyReadNeverWrittenOrLogged()
    {
        foreach (var (relative, text) in Wiring())
        {
            // The passphrase store is read through its getter and never assigned.
            Assert.False(Regex.IsMatch(text, @"VaultPassphraseStore\.Passphrase\s*=[^=>]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), relative + " assigns the passphrase.");
            Assert.DoesNotContain("PutString", text);
            Assert.DoesNotContain("SharedPreferences", text);

            // Every trail call is a literal sentence: no interpolation, no concatenation, no variable.
            foreach (Match call in Regex.Matches(text, @"\bOutcome\(([^;]*)\);", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                var argument = call.Groups[1].Value.Trim();
                if (argument.StartsWith("string ", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    Regex.IsMatch(argument, "^\"[^\"{}]*\"$|^\\$\"[0-9a-z ,]*\\{(?:count|unreadable)\\}[a-z ,]*\"$|^phrase$|^HouseholdSentences\\.UnreadableTrail\\(exception\\.Reason\\)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)),
                    relative + ": a trail call is not a plain sentence: " + argument);
            }
        }
    }

    [Fact]
    public void ThePassphraseIsNamedOnlyByTheDriveStoresGetter()
    {
        var head = Source("Constitution.Android/Household/ConstitutionHousehold.cs");

        Assert.Equal(3, Regex.Matches(head, @"VaultPassphraseStore\.Passphrase|VaultPassphraseStore\.IsSet").Count);
        foreach (var (relative, text) in Wiring().Where(file => file.Relative.StartsWith("Constitution.Infrastructure/", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("VaultPassphraseStore", text);
        }
    }

    [Fact]
    public void MealsIsFedFromTheFile()
    {
        var activity = Source("Constitution.Android/ConstitutionActivity.cs");
        var meals = Source("Constitution.Android/MealsArea.cs");

        Assert.Contains("HouseholdRecipeBox", activity);
        Assert.Contains("HouseholdRecipeKeeper", activity);
        Assert.Contains("mealsArea.Role", activity);
        Assert.Contains("public void Reload()", meals);
    }

    [Fact]
    public void SettingsHasARealRosterNotNull()
    {
        var settings = Source("Constitution.Android/SettingsArea.cs");

        Assert.DoesNotContain("HouseholdGroupModel.Build(null", settings);
        Assert.Contains("ConstitutionHousehold", settings);
    }

    [Fact]
    public void TheFileIsAskedForOnOpenAndOnResume()
    {
        var activity = Source("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("household.Opened()", activity);
        Assert.Contains("household.Resumed()", activity);
    }

    [Fact]
    public void OnlyTheOwnerMayCreateTheSharedFile()
    {
        var head = Source("Constitution.Android/Household/ConstitutionHousehold.cs");

        Assert.Contains("HouseholdSync.MayCreateFile", head);
        Assert.Contains("ConstitutionIdentity.HouseholdFileName", head);
    }
}
