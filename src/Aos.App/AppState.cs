using System.Text.Json;
using Aos.Core;
using Aos.Core.Catalog;
using Aos.Core.Firewall;
using Aos.Core.Game;
using Aos.Core.Learn;
using Aos.Core.Net;

namespace Aos.App;

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public bool RemoveRulesOnExit { get; set; }
    /// <summary>Closing the window hides it to the tray instead of quitting. On by default.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>"System" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public List<string> Blocked { get; set; } = [];
}

/// <summary>Shared state for all pages: catalog, resolved ranges, learned IPs, settings and the firewall.</summary>
public sealed class AppState
{
    private const string SettingsFile = "settings.json";

    public ServerCatalog Catalog { get; private set; } = ServerCatalog.Load();
    public LearnedStore Learned { get; } = new();
    public RangeBuilder Builder { get; } = new();
    public FirewallService Firewall { get; } = new();
    public ServerObserver Observer { get; } = new();
    public AppSettings Settings { get; }
    public RangeSet? Ranges { get; private set; }

    public event Action? RangesChanged;
    public event Action<string, bool>? Notice; // message, isError

    public AppState()
    {
        string path = AppPaths.File(SettingsFile);
        Settings = File.Exists(path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()
            : new();
    }

    public string? GamePath => GameLocator.Find(Settings.GamePath);

    public void SaveSettings() =>
        File.WriteAllText(AppPaths.File(SettingsFile), JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));

    public async Task LoadRangesAsync(bool forceRefresh = false)
    {
        Catalog = ServerCatalog.Load();
        Ranges = await Builder.GetAsync(Catalog, Learned, forceRefresh);
        foreach (var m in Ranges.Messages) Notice?.Invoke(m, true);
        RangesChanged?.Invoke();
    }

    /// <summary>Rebuilds ranges from the cached sources, e.g. after a learned IP is assigned.</summary>
    public void RebuildFromCache()
    {
        Ranges = RangeBuilder.Build(Catalog, Builder.LoadSnapshot(), Learned);
        RangesChanged?.Invoke();
    }

    /// <summary>Writes firewall rules blocking exactly the given servers.</summary>
    public int ApplyBlocks(IReadOnlyCollection<string> blockedCodes)
    {
        if (Ranges is null) throw new InvalidOperationException("IP ranges aren't loaded yet.");
        string game = GamePath ?? throw new FileNotFoundException("Couldn't find Overwatch.exe. Set its location in Settings.");

        var blocked = Catalog.Servers
            .Where(s => blockedCodes.Contains(s.Code, StringComparer.OrdinalIgnoreCase))
            .Select(s => (s.Code, (IReadOnlyList<CidrBlock>)(Ranges.For(s.Code)?.Blocks ?? [])))
            .Where(x => x.Item2.Count > 0)
            .ToList();

        int rules = blocked.Count == 0 ? 0 : Firewall.Apply(game, blocked);
        if (blocked.Count == 0) Firewall.RemoveAll();

        Settings.Blocked = blocked.Select(b => b.Code).ToList();
        SaveSettings();
        return rules;
    }

    public void ReportError(string message) => Notice?.Invoke(message, true);
    public void ReportInfo(string message) => Notice?.Invoke(message, false);
}
