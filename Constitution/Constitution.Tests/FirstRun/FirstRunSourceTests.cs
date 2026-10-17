using System.Text.RegularExpressions;

namespace Constitution.Tests.FirstRun;

/// <summary>Source-level guards on the first-run page (C-8.3.1): plain paper, no echo of the passphrase.</summary>
public class FirstRunSourceTests
{
    private static IReadOnlyList<string> PageFiles() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/FirstRun/", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void TheGuardScansTheFirstRunPage()
    {
        Assert.Contains(PageFiles(), file => file.EndsWith("FirstRunPage.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFirstRunPageIsPlainPaper()
    {
        var offenders = PageFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file),
                @"\b(?:CrinkleField|CrinkleGround\w*|CrinkleSeed|ScrapHost|ScrapView|PageGround)\b"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "First run is not a book page: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ThePassphraseIsNeverLoggedOrToasted()
    {
        var offenders = PageFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"Log\.|Console\.|Toast|Debug\.|WriteLine"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "No logging, toasts or echo on first run: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ThePassphraseGoesOnlyToTheSharedStore()
    {
        // The page itself; the store beside it holds only the equipment-confirmed flag.
        var text = File.ReadAllText(PageFiles().Single(file => file.EndsWith("FirstRunPage.cs", StringComparison.Ordinal)));

        Assert.Contains("VaultPassphraseStore.Passphrase", text);
        Assert.DoesNotContain("PutString", text);
        Assert.DoesNotContain("GetSharedPreferences", text);
    }
}
