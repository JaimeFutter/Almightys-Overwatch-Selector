using Microsoft.UI.Xaml;

namespace Aos.App;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }
    public static AppState State { get; } = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            State.ReportError(e.Exception.Message);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
