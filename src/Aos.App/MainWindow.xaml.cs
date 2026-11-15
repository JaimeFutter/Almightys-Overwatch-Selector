using Aos.App.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aos.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 780));

        App.State.Notice += (message, isError) => DispatcherQueue.TryEnqueue(() =>
        {
            Notice.Severity = isError ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            Notice.Message = message;
            Notice.IsOpen = true;
        });

        Nav.SelectedItem = Nav.MenuItems[0];
        Closed += OnClosed;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                "learn" => typeof(LearnPage),
                _ => typeof(ServersPage),
            };
        if (ContentFrame.CurrentSourcePageType != page) ContentFrame.Navigate(page);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.State.Observer.Stop();
        if (App.State.Settings.RemoveRulesOnExit)
        {
            try { App.State.Firewall.RemoveAll(); } catch { /* closing anyway */ }
        }
    }
}
