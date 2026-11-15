using System.Diagnostics;
using Aos.Core;
using Aos.Core.Game;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Aos.App.Pages;

public sealed partial class SettingsPage : Page
{
    private static AppState State => App.State;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        GamePathBox.Text = State.Settings.GamePath ?? State.GamePath ?? "";
        GameStatus.Text = State.GamePath is { } p ? $"Using {p}" : "Overwatch.exe wasn't found. Browse to it (usually ...\\Overwatch\\_retail_\\Overwatch.exe).";
        RemoveOnExit.IsOn = State.Settings.RemoveRulesOnExit;

        try
        {
            var rules = State.Firewall.List();
            RulesText.Text = rules.Count == 0
                ? "No block rules are active."
                : $"{rules.Count} rules active for {rules.Select(r => r.ServerCode).Distinct().Count()} server(s): " +
                  string.Join(", ", rules.Select(r => r.ServerCode.ToUpperInvariant()).Distinct()) + ". Visible in wf.msc under Outbound/Inbound Rules.";
        }
        catch (Exception e) { RulesText.Text = $"Couldn't read rules: {e.Message}"; }

        var s = State.Builder.LoadSnapshot();
        SourcesText.Text =
            $"Google Cloud: {Fmt(s.GcpFetched)} ({s.Gcp.Count} regions)\n" +
            $"AWS: {Fmt(s.AwsFetched)} ({s.Aws.Count} regions)\n" +
            $"Blizzard network AS57976: {Fmt(s.AsnFetched)} ({s.BlizzardAsn.Count} prefixes)\n" +
            $"Server catalog: {State.Catalog.Origin} ({State.Catalog.Servers.Count} servers)\n" +
            $"Learned server IPs: {State.Learned.Items.Count}\n" +
            "IPs refresh automatically once they're more than 7 days old.";
    }

    private static string Fmt(DateTimeOffset? d) => d is null ? "never downloaded" : $"updated {d.Value.LocalDateTime:g}";

    private void SetGamePath(string? path)
    {
        State.Settings.GamePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim().Trim('"');
        State.SaveSettings();
        Refresh();
    }

    private void GamePath_LostFocus(object sender, RoutedEventArgs e)
    {
        if (GamePathBox.Text != (State.Settings.GamePath ?? State.GamePath ?? "")) SetGamePath(GamePathBox.Text);
    }

    private void Detect_Click(object sender, RoutedEventArgs e) => SetGamePath(GameLocator.Find());

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) SetGamePath(file.Path);
    }

    private void RemoveOnExit_Toggled(object sender, RoutedEventArgs e)
    {
        if (State.Settings.RemoveRulesOnExit == RemoveOnExit.IsOn) return;
        State.Settings.RemoveRulesOnExit = RemoveOnExit.IsOn;
        State.SaveSettings();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await State.LoadRangesAsync(forceRefresh: true);
            State.ReportInfo("IP ranges refreshed. Go to Servers and click Apply to update existing rules.");
        }
        catch (Exception ex) { State.ReportError($"Refresh failed: {ex.Message}"); }
        Refresh();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Ensure()}\"") { UseShellExecute = true });
}
