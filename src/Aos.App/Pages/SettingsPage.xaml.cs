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
        ThemeChoice.SelectedIndex = Array.IndexOf(ThemeKeys, State.Settings.Theme) is var i and >= 0 ? i : 2;
        ThemeSummary.Text = ThemeLabels[ThemeChoice.SelectedIndex];
        AutoUpdateCheck.IsOn = State.Settings.CheckForUpdates;
        _loading = false;
        ShowUpdateStatus();

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
        AboutExpander.Description = $"Version {AppState.CurrentVersion} · GPL-3.0 · Copyright (C) 2026 Jaime Futter";
    }


    private void Repo_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/JaimeFutter/Almightys-Overwatch-Selector") { UseShellExecute = true });

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

    // Same order as the RadioButtons in SettingsPage.xaml.
    private static readonly string[] ThemeKeys = ["Light", "Dark", "System"];
    private static readonly string[] ThemeLabels = ["Light", "Dark", "Use system setting"];

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeChoice.SelectedIndex < 0) return;
        string theme = ThemeKeys[ThemeChoice.SelectedIndex];
        ThemeSummary.Text = ThemeLabels[ThemeChoice.SelectedIndex];
        if (State.Settings.Theme == theme) return;
        State.Settings.Theme = theme;
        State.SaveSettings();
        App.Window?.ApplyTheme(theme);
    }

    private void ShowUpdateStatus()
    {
        string last = State.Settings.LastUpdateCheck is { } t ? $"Last checked {t.LocalDateTime:g}." : "Not checked yet.";
        UpdateCard.Description = State.AvailableUpdate is { } u
            ? $"Version {u.Version} is available (you have {AppState.CurrentVersion}). Use the Download button at the top. {last}"
            : $"You have version {AppState.CurrentVersion}. {last}";
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        UpdateBusy.IsActive = true;
        try { await State.CheckForUpdatesAsync(manual: true); }
        finally
        {
            UpdateBusy.IsActive = false;
            CheckNowButton.IsEnabled = true;
            ShowUpdateStatus();
        }
    }

    private void AutoUpdateCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || State.Settings.CheckForUpdates == AutoUpdateCheck.IsOn) return;
        State.Settings.CheckForUpdates = AutoUpdateCheck.IsOn;
        State.SaveSettings();
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
