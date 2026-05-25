namespace NytRemarkable.Android;

public partial class App : Application
{
    public App(Pages.MainPage mainPage)
    {
        InitializeComponent();
        MainPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#1a1a1a"),
            BarTextColor = Colors.White
        };
    }
}
