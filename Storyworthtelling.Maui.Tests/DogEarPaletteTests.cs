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
