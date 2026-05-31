namespace DailyLoader.Android;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new NavigationPage(
            IPlatformApplication.Current!.Services.GetRequiredService<Pages.MainPage>())
        {
            BarBackgroundColor = Color.FromArgb("#1a1a1a"),
            BarTextColor = Colors.White
        };
    }
}
