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
        // reads a style out of the dictionary. It writes to this instance's own Resources, so it
        // needs nothing from Application.Current — which is the distinction the window below turns
        // on.
        Resources.MergedDictionaries.Add(new PaperKit());
    }

    /// <summary>
    /// Builds the first window — and the first page with it.
    ///
    /// <para><b>This used to happen in the constructor</b>, by assigning <c>MainPage</c>, and that is
    /// both the obsolete API and the wrong moment. Every page in this app reads its colours through
    /// <see cref="PaperResources"/>, which resolves against <c>Application.Current.Resources</c> and
    /// <b>throws</b> when it cannot find a key. Constructing a page inside the constructor asks the
    /// application for its resources while the application is still being built. <c>CreateWindow</c>
    /// runs after it is established, which is why MAUI deprecated the other route.</para>
    ///
    /// <para>The failure that motivated this is unusually cruel: the exception
    /// <c>PaperResources</c> raises says "the kit is not merged", so the one thing it points at is
    /// the line directly above — which is correct, and ran.</para>
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            var page = IPlatformApplication.Current!.Services.GetRequiredService<Pages.MainPage>();
            return new Window(new NavigationPage(page)
            {
                BarBackgroundColor = Colour("Paper"),
                BarTextColor = Colour("Ink"),
            });
        }
        catch (Exception ex)
        {
            // A phone that cannot open the app also cannot reach Settings, and the crash log is in
            // private storage where nobody can read it. So the first screen becomes the report: the
            // one place the owner can actually see why, and read it back.
            return new Window(new Pages.StartupFailurePage(ex));
        }
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
