using Microsoft.Extensions.DependencyInjection;
using Storyworthtelling.Maui;

namespace Folio.Android;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // The shared Paper & Ink kit. Merged here rather than named in App.xaml because a XAML
        // Source= resolves only inside its own assembly, and this must happen before anything
        // reads a style out of the dictionary.
        Resources.MergedDictionaries.Add(new PaperKit());

        MainPage = new NavigationPage(
            IPlatformApplication.Current!.Services.GetRequiredService<Pages.MainPage>())
        {
            BarBackgroundColor = Colour("Paper"),
            BarTextColor = Colour("Ink"),
        };
    }

    /// <summary>
    /// Reads a colour out of the merged dictionaries. This was <c>(Color)Resources["Paper"]</c>, which
    /// worked only while the head owned the dictionary: the indexer looks at a dictionary's own
    /// entries, while <c>TryGetValue</c> walks what has been merged into it — and every token now
    /// arrives by merge.
    /// </summary>
    private Color Colour(string key) =>
        Resources.TryGetValue(key, out var value) && value is Color colour ? colour : Colors.Transparent;
}
