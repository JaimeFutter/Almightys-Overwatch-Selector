using System.Collections.ObjectModel;
using System.Net;
using Aos.Core.Learn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Aos.App.Pages;

public sealed partial class LearnPage : Page
{
    private const string Unknown = "(unknown)";
    private readonly ObservableCollection<LearnedItem> _learned = [];
    private bool _loadingCombos;

    private static AppState State => App.State;

    public LearnPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        LearnedList.ItemsSource = _learned;

        State.Observer.ServerChanged += s => DispatcherQueue.TryEnqueue(() => OnServerChanged(s));
        State.Observer.Tick += top => DispatcherQueue.TryEnqueue(() =>
            TopList.Text = top.Count == 0 ? "No Overwatch traffic" :
                string.Join("\n", top.Select(t => $"{t.Address,-40} {t.PacketsPerSecond,4} pkt/s  {State.Ranges?.Classify(t.Address)?.DisplayName ?? ""}")));
        State.Observer.Error += msg => DispatcherQueue.TryEnqueue(() => State.ReportError($"Learn mode stopped: {msg}"));

        Loaded += (_, _) => { SyncButton(); Reload(); };
    }

    private void Watch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (State.Observer.IsRunning) State.Observer.Stop();
            else State.Observer.Start();
        }
        catch (Exception ex) { State.ReportError($"Couldn't start learn mode: {ex.Message}"); }
        SyncButton();
    }

    private void SyncButton()
    {
        bool on = State.Observer.IsRunning;
        WatchButton.IsChecked = on;
        WatchLabel.Text = on ? "Stop watching" : "Start watching";
        WatchStatus.Text = on ? "Watching Overwatch.exe. Start or join a match." : "Off. Turn on before a match to detect its server.";
    }

    private void OnServerChanged(ObservedServer? s)
    {
        if (s is null)
        {
            CurrentServer.Text = "No match detected";
            CurrentDetail.Text = "Current match server";
            return;
        }

        var server = State.Ranges?.Classify(s.Address);
        var item = State.Learned.Record(s.Address, server?.Code);
        CurrentServer.Text = server is null ? $"{s.Address} · unknown server" : $"{server.DisplayName} · {server.Region}";
        CurrentDetail.Text = $"{s.Address} · {State.Ranges?.DescribeOwner(s.Address)} · ~{s.PacketsPerSecond} packets/s · seen {item.Sightings}×";
        // Seeing a server we already block means its rules have a gap: the learned /24 closes it on the next Apply.
        if (server is not null && State.Settings.Blocked.Contains(server.Code, StringComparer.OrdinalIgnoreCase))
            State.ReportError($"You're on {server.DisplayName}, which is supposed to be blocked. This IP is now learned. Go to Servers and click Apply to close the gap.");
        Reload();
    }

    private void Reload()
    {
        _learned.Clear();
        foreach (var l in State.Learned.Items.OrderByDescending(i => i.LastSeen))
        {
            string owner = State.Ranges?.DescribeOwner(IPAddress.Parse(l.Ip)) ?? "";
            _learned.Add(new LearnedItem(l, owner));
        }
        NoLearned.Visibility = _learned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ServerCombo_Loaded(object sender, RoutedEventArgs e)
    {
        var combo = (ComboBox)sender;
        _loadingCombos = true;
        combo.Items.Clear();
        combo.Items.Add(Unknown);
        foreach (var s in State.Catalog.Servers) combo.Items.Add(s.DisplayName);
        var item = _learned.FirstOrDefault(i => i.Ip == (string)combo.Tag);
        var def = item is null ? null : State.Catalog.Find(item.ServerCode);
        combo.SelectedItem = def?.DisplayName ?? Unknown;
        _loadingCombos = false;
    }

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCombos) return;
        var combo = (ComboBox)sender;
        if (combo.SelectedItem is not string name) return;
        var def = State.Catalog.Servers.FirstOrDefault(s => s.DisplayName == name);
        State.Learned.Assign((string)combo.Tag, def?.Code);
        State.RebuildFromCache();
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        State.Learned.Remove((string)((Button)sender).Tag);
        State.RebuildFromCache();
        Reload();
    }
}
