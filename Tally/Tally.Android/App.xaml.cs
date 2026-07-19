using Microsoft.Extensions.DependencyInjection;

namespace Tally.Android;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new NavigationPage(
            IPlatformApplication.Current!.Services.GetRequiredService<Pages.MainPage>())
        {
            BarBackgroundColor = (Color)Resources["Paper"],
            BarTextColor = (Color)Resources["Ink"]
        };
    }
}
