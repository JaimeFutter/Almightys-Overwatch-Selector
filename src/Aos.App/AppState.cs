using System.Text.Json;
using Aos.Core;
using Aos.Core.Catalog;
using Aos.Core.Firewall;
using Aos.Core.Game;
using Aos.Core.Learn;
using Aos.Core.Net;
using Aos.Core.Updates;

namespace Aos.App;

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public bool RemoveRulesOnExit { get; set; }
    /// <summary>Closing the window hides it to the tray instead of quitting. On by default.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>"System" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    /// <summary>Check GitHub for a newer release at most once a day. On by default.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    /// <summary>The newest version the user has already been notified about, so the tray only pops up once per release.</summary>
    public string? NotifiedVersion { get; set; }
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
    /// <summary>Raised when a newer release is found (automatically or by "Check now").</summary>
    public event Action<ReleaseInfo>? UpdateAvailable;
    public ReleaseInfo? AvailableUpdate { get; private set; }

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(1);

    /// <summary>This build's version, from the assembly's informational version (set by the release build).</summary>
    public static SemVersion CurrentVersion { get; } = SemVersion.TryParse(
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
        out var v) ? v : new SemVersion(0, 0, 0, "dev");

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

    /// <summary>
    /// Checks GitHub for a newer release. Automatic checks respect the setting and run at most once a
    /// day; manual checks always run. Returns the update, or null when up to date (or the check was skipped).
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdatesAsync(bool manual)
    {
        if (!manual && (!Settings.CheckForUpdates ||
            (Settings.LastUpdateCheck is { } last && DateTimeOffset.Now - last < UpdateInterval)))
        {
            // Checked recently: re-announce an update found earlier (e.g. after a restart) without asking GitHub again.
            if (AvailableUpdate is null && Settings.CheckForUpdates &&
                SemVersion.TryParse(Settings.NotifiedVersion, out var known) && known > CurrentVersion)
            {
                AvailableUpdate = new ReleaseInfo(known, $"v{known}", $"{UpdateChecker.ReleasesPage}/tag/v{known}",
                    known.OnPreReleaseChannel, false, null);
                UpdateAvailable?.Invoke(AvailableUpdate);
            }
            return AvailableUpdate;
        }

        try
        {
            var update = await UpdateChecker.CheckAsync(Builder.Http, CurrentVersion);
            Settings.LastUpdateCheck = DateTimeOffset.Now;
            SaveSettings();
            AvailableUpdate = update;
            if (update is not null) UpdateAvailable?.Invoke(update);
            else if (manual) ReportInfo($"You're up to date (version {CurrentVersion}).");
            return update;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (manual) ReportError($"Couldn't check for updates: {e.Message}");
            return null;
        }
    }

    public void ReportError(string message) => Notice?.Invoke(message, true);
    public void ReportInfo(string message) => Notice?.Invoke(message, false);
}
