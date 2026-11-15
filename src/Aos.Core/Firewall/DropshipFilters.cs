using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Aos.Core.Firewall;

public sealed record WfpFilter(Guid Key, ulong Id, string Name, string Layer);

public sealed record DropshipScan(bool Installed, bool Running, IReadOnlyList<WfpFilter> Filters, bool ProviderPresent, string? Error)
{
    public bool HasLeftovers => Filters.Count > 0 || ProviderPresent;
}

/// <summary>
/// dropship blocks servers with Windows Filtering Platform (WFP) filters, which don't show up as firewall
/// rules. They can outlive the app until the next reboot. This finds them (via `netsh wfp show filters`)
/// and deletes them through the WFP API.
/// </summary>
public static class DropshipFilters
{
    // dropship v3's WFP provider and sublayer GUIDs (src/firewall/win/windows_wfp.rs in stowmyy/dropship).
    public static readonly Guid ProviderKey = new("9f8b0434-3b49-4cb0-9e1a-d1e7ba2e05d2");
    public static readonly Guid SubLayerKey = new("9d313d65-76a7-4213-8967-bd604d64d47c");

    public static DropshipScan Scan()
    {
        bool installed = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dropship"));
        bool running = Process.GetProcessesByName("dropship").Length > 0;

        if (!FirewallService.IsElevated())
            return new DropshipScan(installed, running, [], false, "Checking WFP filters needs administrator rights.");

        string file = Path.Combine(Path.GetTempPath(), $"aos-wfp-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo("netsh", $"wfp show filters file=\"{file}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var p = Process.Start(psi)!)
            {
                p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(60_000)) { p.Kill(); return new DropshipScan(installed, running, [], false, "netsh timed out."); }
            }
            if (!File.Exists(file)) return new DropshipScan(installed, running, [], false, "netsh didn't produce a filter list.");

            var (filters, provider) = Parse(XDocument.Load(file));
            return new DropshipScan(installed, running, filters, provider, null);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        {
            return new DropshipScan(installed, running, [], false, e.Message);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>Picks dropship's filters out of `netsh wfp show filters` XML.</summary>
    public static (List<WfpFilter> Filters, bool ProviderSeen) Parse(XDocument doc)
    {
        var filters = new List<WfpFilter>();
        bool providerSeen = false;
        foreach (var item in doc.Descendants("item").Where(i => i.Element("filterKey") is not null))
        {
            string name = item.Element("displayData")?.Element("name")?.Value ?? "";
            bool byProvider = Guid.TryParse(item.Element("providerKey")?.Value, out var pk) && pk == ProviderKey;
            bool bySubLayer = Guid.TryParse(item.Element("subLayerKey")?.Value, out var sk) && sk == SubLayerKey;
            if (!byProvider && !bySubLayer && !name.Contains("dropship", StringComparison.OrdinalIgnoreCase)) continue;

            providerSeen |= byProvider;
            ulong.TryParse(item.Element("filterId")?.Value, out ulong id);
            filters.Add(new WfpFilter(Guid.Parse(item.Element("filterKey")!.Value), id, name, item.Element("layerKey")?.Value ?? ""));
        }
        return (filters, providerSeen);
    }

    /// <summary>Deletes dropship's filters, then its sublayer and provider. Returns the number of filters removed.</summary>
    public static int RemoveAll(IEnumerable<WfpFilter> filters)
    {
        if (!FirewallService.IsElevated())
            throw new UnauthorizedAccessException("Removing WFP filters needs administrator rights.");

        uint err = FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, IntPtr.Zero, out var engine);
        if (err != 0) throw new InvalidOperationException($"Couldn't open the Windows Filtering Platform (0x{err:X8}).");
        int removed = 0;
        try
        {
            foreach (var f in filters)
            {
                var key = f.Key;
                err = FwpmFilterDeleteByKey0(engine, ref key);
                if (err == 0) removed++;
                else if (err != FwpEFilterNotFound)
                    throw new InvalidOperationException($"Couldn't remove filter '{f.Name}' (0x{err:X8}). Close dropship and try again.");
            }
            var sub = SubLayerKey;
            err = FwpmSubLayerDeleteByKey0(engine, ref sub);       // fine if missing or still in use
            var prov = ProviderKey;
            err = FwpmProviderDeleteByKey0(engine, ref prov);
        }
        finally { FwpmEngineClose0(engine); }
        return removed;
    }

    private const uint RpcCAuthnWinnt = 10;
    private const uint FwpEFilterNotFound = 0x80320003;

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmProviderDeleteByKey0(IntPtr engineHandle, ref Guid key);
}
