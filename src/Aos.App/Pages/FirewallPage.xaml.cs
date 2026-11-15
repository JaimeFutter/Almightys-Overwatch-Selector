using Aos.Core.Firewall;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aos.App.Pages;

public sealed partial class FirewallPage : Page
{
    private List<OverwatchRule> _rules = [];
    private DropshipScan? _dropship;
    private bool _scanning;

    private static AppState State => App.State;

    public FirewallPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ScanAsync(includeWfp: true);
    }

    private async Task ScanAsync(bool includeWfp)
    {
        if (_scanning) return;
        _scanning = true;
        Busy.IsActive = true;
        try
        {
            _rules = (await Task.Run(OverwatchRules.List))
                .OrderBy(r => r.Source).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (includeWfp) _dropship = await Task.Run(DropshipFilters.Scan);
        }
        catch (Exception e) { State.ReportError($"Couldn't read firewall rules: {e.Message}"); }
        finally
        {
            Busy.IsActive = false;
            _scanning = false;
        }
        Render();
    }

    private void Render()
    {
        Groups.ItemsSource = _rules
            .GroupBy(r => r.Source)
            .OrderBy(g => g.Key)
            .Select(g => new RuleGroup { Name = g.First().SourceText, Rules = g.ToList() })
            .ToList();
        EmptyText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var blocks = _rules.Where(r => r.IsBlock).ToList();
        RemoveBlocksButton.IsEnabled = blocks.Count > 0;
        RemoveAllCard.Description = blocks.Count == 0
            ? "Nothing is blocking Overwatch."
            : $"Deletes {blocks.Count} block rule(s): " +
              string.Join(", ", blocks.GroupBy(r => r.SourceText).Select(g => $"{g.Count()} from {g.Key}")) +
              ". Windows' allow rules are kept.";

        if (_dropship is { HasLeftovers: true } d)
        {
            DropshipBar.Message = $"dropship has {d.Filters.Count} WFP filter(s) active. They block servers outside Windows Firewall, so they aren't listed below and can override your choices."
                + (d.Running ? " dropship is running: close it first, or unblock everything in dropship." : "");
            DropshipBar.IsOpen = true;
        }
        else DropshipBar.IsOpen = false;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync(includeWfp: true);

    private async void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        // Read the control on the UI thread; XAML objects throw RPC_E_WRONG_THREAD from the thread pool.
        var toggle = (ToggleSwitch)sender;
        string key = (string)toggle.Tag;
        bool on = toggle.IsOn;

        // Toggled also fires when a recycled row's switch gets its initial value. Only act on real changes,
        // otherwise every re-render would trigger another rescan, forever.
        var rule = _rules.FirstOrDefault(r => r.Key == key);
        if (rule is null || rule.Enabled == on) return;

        try { await Task.Run(() => OverwatchRules.SetEnabled([key], on)); }
        catch (Exception ex) { State.ReportError(ex.Message); }
        await ScanAsync(includeWfp: false);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var rule = _rules.FirstOrDefault(r => r.Key == (string)((Button)sender).Tag);
        if (rule is null) return;

        string warning = rule.Source == RuleSource.WindowsPrompt
            ? "\n\nThis is Windows' normal 'allow Overwatch' rule. Without it, Windows may ask again or block incoming connections."
            : "";
        if (!await ConfirmAsync("Delete rule?", $"Delete \"{rule.Name}\" ({rule.ActionText} {rule.DirectionText}, made by {rule.SourceText})? This can't be undone.{warning}", "Delete"))
            return;

        string key = rule.Key;
        try { await Task.Run(() => OverwatchRules.Remove([key])); State.ReportInfo($"Deleted \"{rule.Name}\"."); }
        catch (Exception ex) { State.ReportError(ex.Message); }
        await ScanAsync(includeWfp: false);
    }

    private async void RemoveBlocks_Click(object sender, RoutedEventArgs e)
    {
        var blocks = _rules.Where(r => r.IsBlock).ToList();
        var bySource = blocks.GroupBy(r => r.SourceText).Select(g => $"• {g.Count()} from {g.Key}");
        if (!await ConfirmAsync("Remove all Overwatch block rules?",
                $"This deletes {blocks.Count} block rule(s):\n{string.Join("\n", bySource)}\n\n" +
                "Windows' allow rules are kept. Overwatch will be able to reach every server until you Apply again. This can't be undone.",
                "Remove all"))
            return;

        try
        {
            int n = await Task.Run(OverwatchRules.RemoveAllBlocks);
            State.Settings.Blocked = [];
            State.SaveSettings();
            State.ReportInfo($"Removed {n} block rule(s). Overwatch can reach every server.");
        }
        catch (Exception ex) { State.ReportError(ex.Message); }
        await ScanAsync(includeWfp: false);
    }

    private async void RemoveDropship_Click(object sender, RoutedEventArgs e)
    {
        if (_dropship is null) return;
        var filters = _dropship.Filters;
        if (!await ConfirmAsync("Remove dropship filters?",
                $"Deletes {filters.Count} WFP filter(s) that dropship left behind. dropship's own settings stay; if you open dropship again it may re-add them.",
                "Remove"))
            return;

        try
        {
            int n = await Task.Run(() => DropshipFilters.RemoveAll(filters));
            State.ReportInfo($"Removed {n} dropship filter(s).");
        }
        catch (Exception ex) { State.ReportError(ex.Message); }
        await ScanAsync(includeWfp: true);
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
