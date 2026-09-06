using Storyworthtelling.Maui;

namespace Foreword.Android;

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
    /// Reads a colour out of the merged dictionaries. The indexer looks in the dictionary's own
    /// entries; <c>TryGetValue</c> walks what has been merged into it, which is where every token
    /// now lives.
    /// </summary>
    private Color Colour(string key) =>
        Resources.TryGetValue(key, out var value) && value is Color colour ? colour : Colors.Transparent;
}
