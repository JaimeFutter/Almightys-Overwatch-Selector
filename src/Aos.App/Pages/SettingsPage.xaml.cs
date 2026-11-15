using System.Diagnostics;
using Aos.Core;
using Aos.Core.Game;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Aos.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    private static AppState State => App.State;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _loading = true;
        GameCard.Description = State.GamePath is { } p
            ? p
            : "Not found. Click Browse and pick ...\\Overwatch\\_retail_\\Overwatch.exe.";
        CloseToTray.IsOn = State.Settings.CloseToTray;
        RemoveOnExit.IsOn = State.Settings.RemoveRulesOnExit;
        _loading = false;

        try
        {
            var rules = State.Firewall.List();
            RulesCard.Description = rules.Count == 0
                ? "No block rules are active. Overwatch can reach every server."
                : $"{rules.Count} rules blocking {rules.Select(r => r.ServerCode).Distinct().Count()} server(s): " +
                  string.Join(", ", rules.Select(r => r.ServerCode.ToUpperInvariant()).Distinct()) + ".";
        }
        catch (Exception e) { RulesCard.Description = $"Couldn't read rules: {e.Message}"; }

        var s = State.Builder.LoadSnapshot();
        GcpCard.Description = $"{Fmt(s.GcpFetched)} · {s.Gcp.Count} regions";
        AwsCard.Description = $"{Fmt(s.AwsFetched)} · {s.Aws.Count} regions";
        AsnCard.Description = $"{Fmt(s.AsnFetched)} · {s.BlizzardAsn.Count} prefixes";
        CatalogCard.Description = $"{State.Catalog.Origin} · {State.Catalog.Servers.Count} servers";
        LearnedCard.Description = $"{State.Learned.Items.Count} learned";
        DataCard.Description = AppPaths.DataDirectory;
    }

    private static string Fmt(DateTimeOffset? d) => d is null ? "Never downloaded" : $"Updated {d.Value.LocalDateTime:g}";

    private void SetGamePath(string? path)
    {
        State.Settings.GamePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim().Trim('"');
        State.SaveSettings();
        Refresh();
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        string? found = GameLocator.Find();
        if (found is null) State.ReportError("Couldn't find Overwatch.exe automatically. Use Browse to pick it.");
        SetGamePath(found);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) SetGamePath(file.Path);
    }

    private void CloseToTray_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || State.Settings.CloseToTray == CloseToTray.IsOn) return;
        State.Settings.CloseToTray = CloseToTray.IsOn;
        State.SaveSettings();
    }

    private void RemoveOnExit_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || State.Settings.RemoveRulesOnExit == RemoveOnExit.IsOn) return;
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
