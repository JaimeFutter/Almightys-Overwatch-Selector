using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aos.Core;
using Aos.Core.Catalog;
using Aos.Core.Learn;
using Microsoft.UI.Xaml;

namespace Aos.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ServerItem : Observable
{
    public ServerItem(ServerDefinition def) => Definition = def;

    public ServerDefinition Definition { get; }
    public string Code => Definition.Code;
    public string Title => Definition.DisplayName;
    public string Location => Definition.Location;
    public Visibility ExperimentalVisibility => Definition.Experimental ? Visibility.Visible : Visibility.Collapsed;

    private bool _allowed = true;
    public bool IsAllowed
    {
        get => _allowed;
        set { if (Set(ref _allowed, value)) { Raise(nameof(StateText)); Raise(nameof(StateGlyph)); AllowedChanged?.Invoke(); } }
    }
    public string StateText => _allowed ? "Allowed" : "Blocked";
    /// <summary>Globe when allowed, blocked-circle when blocked.</summary>
    public string StateGlyph => _allowed ? "\uE774" : "\uE733";
    public event Action? AllowedChanged;

    private bool _applied = true;
    /// <summary>Whether the firewall currently allows this server (what's actually in effect).</summary>
    public bool AppliedAllowed { get => _applied; set => Set(ref _applied, value); }

    private string _ping = "—";
    public string PingText { get => _ping; set => Set(ref _ping, value); }

    private string _ranges = "";
    public string RangeText { get => _ranges; set => Set(ref _ranges, value); }

    private string _warning = "";
    public string WarningText
    {
        get => _warning;
        set { if (Set(ref _warning, value)) Raise(nameof(WarningVisibility)); }
    }
    public Visibility WarningVisibility => string.IsNullOrEmpty(_warning) ? Visibility.Collapsed : Visibility.Visible;

    public void Update(ServerRanges? r)
    {
        if (r is null) { RangeText = "no ranges"; return; }
        var parts = new List<string>();
        if (r.BlizzardCount > 0) parts.Add($"Blizzard {r.BlizzardCount}");
        if (r.GcpCount > 0) parts.Add($"Google Cloud {r.GcpCount}");
        if (r.AwsCount > 0) parts.Add($"AWS {r.AwsCount}");
        if (r.LearnedCount > 0) parts.Add($"learned {r.LearnedCount}");
        RangeText = $"{r.Blocks.Count} ranges · {string.Join(" · ", parts)}";
        WarningText = string.Join("\n", r.Warnings);
    }
}

public sealed class RegionGroup
{
    public required string Name { get; init; }
    public ObservableCollection<ServerItem> Servers { get; } = [];
}

public sealed class LearnedItem : Observable
{
    public LearnedItem(LearnedServer s, string owner)
    {
        Source = s;
        Owner = owner;
    }

    public LearnedServer Source { get; }
    public string Ip => Source.Ip;
    public string Owner { get; }
    public string Seen => $"{Source.Sightings}× · last {Source.LastSeen.LocalDateTime:g}";
    public string ServerCode => Source.ServerCode ?? "";
}

public sealed class RuleGroup
{
    public required string Name { get; init; }
    public required List<Aos.Core.Firewall.OverwatchRule> Rules { get; init; }
    public string CountText => $"{Rules.Count} rule{(Rules.Count == 1 ? "" : "s")}";

    public static string GlyphFor(bool isBlock) => isBlock ? "\uE733" : "\uE73E";
}
