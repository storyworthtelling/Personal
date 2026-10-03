using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

public class DogEarPaletteTests
{
    /// <summary>
    /// Every hue a fold can carry resolves to a token, and each token is one the kit's dictionary
    /// actually declares. A name that resolves to nothing throws at the moment the corner folds —
    /// which is to say, at the moment something has already gone wrong.
    /// </summary>
    [Theory]
    [InlineData(DogEarHue.Problem)]
    [InlineData(DogEarHue.Caution)]
    [InlineData(DogEarHue.Resolved)]
    [InlineData(DogEarHue.Action)]
    public void EveryHueNamesATokenTheDictionaryDeclares(DogEarHue hue)
    {
        var token = DogEarPalette.TokenFor(hue);

        Assert.Contains(
            $"<Color x:Key=\"{token}\">",
            File.ReadAllText(KitDictionary()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// R6 / mockup 17: the Waiting page needs slate for "only worth knowing" (Reference) and teal for
    /// "waiting, not wrong" (Progress, the role Score's Playing area and Index's Snowball wear).
    /// Asserted by name so the enum cannot lose one without this failing.
    /// </summary>
    [Theory]
    [InlineData("Reference", "ReferenceMark")]
    [InlineData("Progress", "ProgressMark")]
    public void TheWaitingPageHuesExistAndNameTheirRoleMarks(string hue, string token)
    {
        Assert.Contains(hue, Enum.GetNames<DogEarHue>());
        Assert.Equal(token, DogEarPalette.TokenFor(Enum.Parse<DogEarHue>(hue)));
        Assert.Contains($"<Color x:Key=\"{token}\">", File.ReadAllText(KitDictionary()), StringComparison.Ordinal);
    }

    private static string KitDictionary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName, "Storyworthtelling.Maui", "Resources", "Styles", "Paper.xaml");
    }
}
