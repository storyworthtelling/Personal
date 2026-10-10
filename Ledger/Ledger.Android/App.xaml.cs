using Microsoft.Extensions.DependencyInjection;
using Storyworthtelling.Maui;

namespace Index.Android;

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
    /// <para><b>This used to happen in the constructor</b>, by assigning <c>MainPage</c>, and that
    /// is both the obsolete API and the wrong moment. Every page in this app reads its colours
    /// through <see cref="PaperResources"/>, which resolves against
    /// <c>Application.Current.Resources</c> and <b>throws</b> when it cannot find a key.
    /// Constructing a page inside the constructor asks the application for its resources while the
    /// application is still being built. <c>CreateWindow</c> runs after it is established, which is
    /// why MAUI deprecated the other route.</para>
    ///
    /// <para>A fault from an earlier launch is shown first, in full, before the owner reaches the
    /// first page; the report stays until they continue. The first page itself is built under a guard
    /// (see <see cref="FirstPage"/>) so a fault while building it is shown instead of ending the app.</para>
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var pending = LaunchFaults.PendingReport();
        return new Window(pending is null ? FirstPage() : new Pages.LaunchFaultPage(pending, FirstPage));
    }

    /// <summary>
    /// The first real page, or the fault that stopped it being built. Building it is the step most
    /// likely to fail at launch, and a failure here used to end the process before anything was on
    /// screen. The fault is recorded (so the next launch shows it) and shown now.
    /// </summary>
    private Page FirstPage()
    {
        try
        {
            var page = IPlatformApplication.Current!.Services.GetRequiredService<Pages.MainPage>();
            return new NavigationPage(page)
            {
                BarBackgroundColor = Colour("Paper"),
                BarTextColor = Colour("Ink"),
            };
        }
        catch (Exception ex)
        {
            var report = LaunchFaults.Record("Building the first page", ex);
            return new Pages.LaunchFaultPage(report, continueTo: null);
        }
    }

    /// <summary>
    /// Reads a colour out of the merged dictionaries. This was <c>(Color)Resources["Paper"]</c>,
    /// which worked only while the head owned the dictionary: the indexer looks at a dictionary's
    /// own entries, while <c>TryGetValue</c> walks what has been merged into it — and every token
    /// now arrives by merge.
    /// </summary>
    private Color Colour(string key) =>
        Resources.TryGetValue(key, out var value) && value is Color colour ? colour : Colors.Transparent;
}
