using Aos.App.Pages;
using Aos.Core.Updates;
using Microsoft.UI.Dispatching;
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
    private readonly DispatcherQueueTimer _updateTimer;

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

        // Update checks: shortly after start, then every few hours while running (e.g. sitting in the tray).
        // AppState only actually contacts GitHub once a day, and never when the setting is off.
        App.State.UpdateAvailable += update => DispatcherQueue.TryEnqueue(() => ShowUpdate(update));
        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromHours(6);
        _updateTimer.Tick += async (_, _) => await App.State.CheckForUpdatesAsync(manual: false);
        _updateTimer.Start();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await App.State.CheckForUpdatesAsync(manual: false);
        });

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

    private void ShowUpdate(ReleaseInfo update)
    {
        UpdateBar.Message = $"Version {update.Version} is available (you have {AppState.CurrentVersion}).";
        UpdateBar.Tag = update.Url;
        UpdateBar.IsOpen = true;

        // Pop a tray notification once per new version, so people running in the tray hear about it too.
        if (App.State.Settings.NotifiedVersion != update.Version.ToString())
        {
            App.State.Settings.NotifiedVersion = update.Version.ToString();
            App.State.SaveSettings();
            if (!AppWindow.IsVisible)
                _tray?.Notify("Update available", $"Almighty's Overwatch Selector {update.Version} is out. Open the app to download it.");
        }
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            UpdateBar.Tag as string ?? UpdateChecker.ReleasesPage) { UseShellExecute = true });

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
        _updateTimer.Stop();
        _tray?.Dispose();
        App.State.Observer.Stop();
        if (App.State.Settings.RemoveRulesOnExit)
        {
            try { App.State.Firewall.RemoveAll(); } catch { /* closing anyway */ }
        }
    }
}
