using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Aos.Core.Game;

/// <summary>Finds Overwatch.exe for Battle.net and Steam installs.</summary>
public static partial class GameLocator
{
    public const string ExeName = "Overwatch.exe";
    public const string SteamAppId = "2357570";

    public static string? Find(string? preferred = null) => Candidates(preferred).FirstOrDefault(File.Exists);

    public static IEnumerable<string> Candidates(string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) yield return preferred;

        // A running game tells us exactly where it is.
        foreach (var p in SafeProcesses())
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied without elevation */ }
            if (path is not null) yield return path;
        }

        // Battle.net registers an uninstall entry.
        foreach (string root in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch",
                                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key?.GetValue("InstallLocation") is string dir && dir.Length > 0)
            {
                yield return Path.Combine(dir, ExeName);
                yield return Path.Combine(dir, "_retail_", ExeName);
            }
        }

        // Steam: walk every library folder.
        foreach (string lib in SteamLibraries())
            yield return Path.Combine(lib, "steamapps", "common", "Overwatch", ExeName);

        // Common default folders on every fixed drive.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            string r = drive.RootDirectory.FullName;
            yield return Path.Combine(r, "Program Files (x86)", "Overwatch", "_retail_", ExeName);
            yield return Path.Combine(r, "Program Files (x86)", "Overwatch", ExeName);
            yield return Path.Combine(r, "Program Files", "Overwatch", "_retail_", ExeName);
            yield return Path.Combine(r, "Games", "Overwatch", "_retail_", ExeName);
            yield return Path.Combine(r, "Overwatch", "_retail_", ExeName);
        }
    }

    public static int[] RunningProcessIds() => SafeProcesses().Select(p => p.Id).ToArray();

    private static IEnumerable<Process> SafeProcesses()
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)); }
        catch { return []; }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        string? steam = null;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            steam = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        if (steam is null) yield break;
        yield return steam;

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();
}
