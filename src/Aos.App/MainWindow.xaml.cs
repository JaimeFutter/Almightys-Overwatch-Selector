using Aos.App.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aos.App;

public sealed partial class MainWindow : Window
{
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    private readonly TrayIcon? _tray;
    private bool _exiting;
    private bool _toldAboutTray;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));
        if (File.Exists(IconPath)) AppWindow.SetIcon(IconPath); // window bar, taskbar, Alt+Tab
        ApplyTheme(App.State.Settings.Theme);

        App.State.Notice += (message, isError) => DispatcherQueue.TryEnqueue(() =>
        {
            Notice.Severity = isError ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            Notice.Message = message;
            Notice.IsOpen = true;
        });

        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), IconPath, "Almighty's Overwatch Selector");
            _tray.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowFromTray);
            _tray.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);
        }
        catch (Exception e) { App.State.ReportError($"Couldn't create the tray icon: {e.Message}"); }

        Nav.SelectedItem = Nav.MenuItems[0];
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                "learn" => typeof(LearnPage),
                "firewall" => typeof(FirewallPage),
                _ => typeof(ServersPage),
            };
        if (ContentFrame.CurrentSourcePageType != page) ContentFrame.Navigate(page);
    }

    /// <summary>The X button hides to the tray when that setting is on; Exit in the tray menu really quits.</summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || _tray is null || !App.State.Settings.CloseToTray) return;

        args.Cancel = true;
        AppWindow.Hide();
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            _tray.Notify("Still running in the tray",
                "Your server blocks stay active. Click the tray icon to open the app, or right-click it and choose Exit. You can turn this off in Settings.");
        }
    }

    /// <summary>
    /// Applies "Light", "Dark" or "System". The content's RequestedTheme drives every page and the Mica
    /// backdrop; the caption buttons (min/max/close) are drawn by the system, so the title bar gets
    /// the matching theme too.
    /// </summary>
    public void ApplyTheme(string theme)
    {
        var (element, titleBar) = theme switch
        {
            "Light" => (ElementTheme.Light, TitleBarTheme.Light),
            "Dark" => (ElementTheme.Dark, TitleBarTheme.Dark),
            _ => (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode),
        };
        if (Content is FrameworkElement root) root.RequestedTheme = element;
        AppWindow.TitleBar.PreferredTheme = titleBar;
    }

    public void ShowFromTray()
    {
        AppWindow.Show(true);
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _tray?.Dispose();
        App.State.Observer.Stop();
        if (App.State.Settings.RemoveRulesOnExit)
        {
            try { App.State.Firewall.RemoveAll(); } catch { /* closing anyway */ }
        }
    }
}
