using System.Collections.ObjectModel;
using Aos.Core.Ping;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Aos.App.Pages;

public sealed partial class ServersPage : Page
{
    private readonly ObservableCollection<RegionGroup> _groups = [];
    private List<ServerItem> _items = [];
    private bool _suppress;
    private bool _rangesDirty; // IPs refreshed since the rules were written

    private static AppState State => App.State;

    public ServersPage()
    {
        InitializeComponent();
        Groups.ItemsSource = _groups;
        NavigationCacheMode = NavigationCacheMode.Required;
        State.RangesChanged += () => DispatcherQueue.TryEnqueue(UpdateRanges);
        Loaded += async (_, _) => { if (_items.Count == 0) await InitializeAsync(); };
    }

    private async Task InitializeAsync()
    {
        BuildItems();
        Busy.IsActive = true;
        try { await State.LoadRangesAsync(); }
        catch (Exception e) { State.ReportError($"Couldn't load IP ranges: {e.Message}"); }
        finally { Busy.IsActive = false; }
        ReadFirewall();
        _ = PingAllAsync();
    }

    private void BuildItems()
    {
        _groups.Clear();
        _items = State.Catalog.Servers.Select(s => new ServerItem(s)).ToList();
        foreach (var region in _items.GroupBy(i => i.Definition.Region))
        {
            var g = new RegionGroup { Name = RegionTitle(region.Key) };
            foreach (var item in region)
            {
                item.AllowedChanged += UpdateStatus;
                g.Servers.Add(item);
            }
            _groups.Add(g);
        }

        PresetMenu.Items.Clear();
        foreach (var region in State.Catalog.Regions)
        {
            var mi = new MenuFlyoutItem { Text = RegionTitle(region), Tag = region };
            mi.Click += (s, _) => OnlyAllow((string)((MenuFlyoutItem)s).Tag);
            PresetMenu.Items.Add(mi);
        }
    }

    private static string RegionTitle(string region) => region switch
    {
        "NA" => "North America",
        "EU" => "Europe",
        "ME" => "Middle East",
        _ => region,
    };

    private void UpdateRanges()
    {
        foreach (var item in _items) item.Update(State.Ranges?.For(item.Code));
        UpdateStatus();
    }

    /// <summary>Sets toggles to match what the firewall is actually enforcing right now.</summary>
    private void ReadFirewall()
    {
        IReadOnlySet<string> blocked;
        try { blocked = State.Firewall.BlockedServers(); }
        catch (Exception e) { State.ReportError($"Couldn't read firewall rules: {e.Message}"); return; }

        _suppress = true;
        foreach (var item in _items)
        {
            item.AppliedAllowed = !blocked.Contains(item.Code);
            item.IsAllowed = item.AppliedAllowed;
        }
        _suppress = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_suppress) return;
        int pending = _items.Count(i => i.IsAllowed != i.AppliedAllowed);
        string game = State.GamePath is { } p ? $"Game: {p}" : "Overwatch.exe not found — set it in Settings";
        string applied = _items.Count(i => !i.AppliedAllowed) is var a && a > 0 ? $"{a} server(s) blocked now" : "nothing blocked now";
        bool stale = _rangesDirty && _items.Any(i => !i.AppliedAllowed);
        StatusText.Text = $"{applied} · {game}"
            + (pending > 0 ? $" · {pending} unapplied change(s)" : "")
            + (stale ? " · new IPs downloaded, Apply to update rules" : "");
        ApplyButton.IsEnabled = pending > 0 || stale;
        UnblockAllButton.IsEnabled = _items.Any(i => !i.AppliedAllowed);
    }

    private void SetAllowed(Func<ServerItem, bool> allowed)
    {
        _suppress = true;
        foreach (var item in _items) item.IsAllowed = allowed(item);
        _suppress = false;
        UpdateStatus();
    }

    private void OnlyAllow(string region) => SetAllowed(i => i.Definition.Region == region);

    private void BlockMiddleEast_Click(object sender, RoutedEventArgs e) =>
        SetAllowed(i => i.Definition.Region == "ME" ? false : i.IsAllowed);

    private void AllowAll_Click(object sender, RoutedEventArgs e) => SetAllowed(_ => true);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var blocked = _items.Where(i => !i.IsAllowed).Select(i => i.Code).ToList();
        try
        {
            int rules = State.ApplyBlocks(blocked);
            State.ReportInfo(blocked.Count == 0
                ? "All servers allowed. Firewall rules removed."
                : $"Blocked {blocked.Count} server(s) with {rules} firewall rules. Restart Overwatch if it's running so it re-picks a server.");
        }
        catch (Exception ex) { State.ReportError(ex.Message); }
        _rangesDirty = false;
        ReadFirewall();
    }

    private void UnblockAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            State.Firewall.RemoveAll();
            State.Settings.Blocked = [];
            State.SaveSettings();
            State.ReportInfo("All firewall rules removed. Overwatch can reach every server.");
        }
        catch (Exception ex) { State.ReportError(ex.Message); }
        ReadFirewall();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        try
        {
            await State.LoadRangesAsync(forceRefresh: true);
            State.ReportInfo($"IP ranges refreshed from Google Cloud, AWS and Blizzard's network. Click Apply to update existing rules.");
            _rangesDirty = true;
            UpdateStatus();
        }
        catch (Exception ex) { State.ReportError($"Refresh failed: {ex.Message}"); }
        finally { Busy.IsActive = false; }
    }

    private async void Ping_Click(object sender, RoutedEventArgs e) => await PingAllAsync();

    private async Task PingAllAsync()
    {
        foreach (var item in _items) item.PingText = item.Definition.Ping is null ? "no ping host" : "pinging…";
        await Task.WhenAll(_items.Where(i => i.Definition.Ping is not null).Select(async item =>
        {
            int? ms = await LatencyProbe.MeasureAsync(item.Definition.Ping!);
            DispatcherQueue.TryEnqueue(() => item.PingText = ms is null ? "no reply" : $"{ms} ms");
        }));
    }
}
