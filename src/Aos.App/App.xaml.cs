using Microsoft.UI.Xaml;

namespace Aos.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\AlmightysOverwatchSelector";

    public static MainWindow? Window { get; private set; }
    public static AppState State { get; } = new();

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;

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
        // One instance only: a second launch (e.g. while hidden in the tray) brings the first one back.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(InstanceName + ".show").Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Exit();
            return;
        }

        Window = new MainWindow();
        Window.Activate();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".show");
        new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Window.DispatcherQueue.TryEnqueue(Window.ShowFromTray);
        }) { IsBackground = true, Name = "AOS single instance" }.Start();
    }
}
