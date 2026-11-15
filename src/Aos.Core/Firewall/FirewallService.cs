using System.Runtime.InteropServices;
using System.Security.Principal;
using Aos.Core.Net;

namespace Aos.Core.Firewall;

public sealed record FirewallRuleInfo(string Name, string ServerCode, string Direction, int AddressCount, string Program, bool Enabled);

/// <summary>
/// Creates Windows Firewall block rules scoped to Overwatch.exe through the HNetCfg.FwPolicy2 COM API.
/// Every rule is named "AOS - CODE - n dir" in one group, so we only ever touch our own rules.
/// Never put '|' in a rule's name or description: Windows stores each rule as a '|'-separated
/// record and rejects such rules with E_INVALIDARG ("Value does not fall within the expected range").
/// </summary>
public sealed class FirewallService
{
    public const string Group = "Almighty's Overwatch Selector";
    public const string Prefix = "AOS - ";
    /// <summary>Addresses per rule. Windows accepts more, but big rules get slow to create and to edit in wf.msc.</summary>
    public const int MaxAddressesPerRule = 1000;

    private const int ProtocolAny = 256;
    private const int DirectionIn = 1, DirectionOut = 2;
    private const int ActionBlock = 0;
    private const int ProfilesAll = 0x7FFFFFFF;

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Splits a block list into rule-sized chunks of address strings.</summary>
    public static List<List<string>> Chunk(IEnumerable<CidrBlock> blocks, int size = MaxAddressesPerRule) =>
        blocks.Select(b => b.ToString()).Chunk(size).Select(c => c.ToList()).ToList();

    public static string RuleName(string serverCode, int index, string direction) =>
        $"{Prefix}{serverCode.ToUpperInvariant()} - {index + 1} {direction}";

    /// <summary>Extracts the server code from one of our rule names ("AOS - MES1 - 1 out" -> "mes1").</summary>
    public static string ServerCodeOf(string ruleName) =>
        ruleName.StartsWith(Prefix, StringComparison.Ordinal)
            ? ruleName[Prefix.Length..].Split(' ', 2)[0].ToLowerInvariant()
            : "";

    /// <summary>Replaces all our rules with block rules for the given servers.</summary>
    public int Apply(string programPath, IEnumerable<(string ServerCode, IReadOnlyList<CidrBlock> Blocks)> blocked)
    {
        EnsureElevated();
        if (!File.Exists(programPath)) throw new FileNotFoundException("Overwatch.exe not found.", programPath);

        RemoveAll();
        dynamic policy = CreatePolicy();
        int created = 0;
        string? current = null;
        try
        {
            foreach (var (code, blocks) in blocked)
            {
                var chunks = Chunk(blocks);
                for (int i = 0; i < chunks.Count; i++)
                {
                    string addresses = string.Join(",", chunks[i]);
                    foreach (var (dir, label) in new[] { (DirectionOut, "out"), (DirectionIn, "in") })
                    {
                        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
                        rule.Name = current = RuleName(code, i, label);
                        rule.Description = $"Blocks Overwatch from {code.ToUpperInvariant()} servers. Managed by {Group}.";
                        rule.Grouping = Group;
                        rule.ApplicationName = programPath;
                        rule.Protocol = ProtocolAny;
                        rule.Direction = dir;
                        rule.Action = ActionBlock;
                        rule.Profiles = ProfilesAll;
                        rule.RemoteAddresses = addresses;
                        rule.Enabled = true;
                        policy.Rules.Add(rule);
                        Marshal.FinalReleaseComObject(rule);
                        created++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            // Don't leave a half-applied block list behind.
            Marshal.FinalReleaseComObject(policy);
            policy = null!; // released above; the finally block must not release it twice
            try { RemoveAll(); } catch { /* report the original error */ }
            throw new InvalidOperationException($"Windows Firewall rejected rule '{current}': {e.Message} No rules were left in place.", e);
        }
        finally { if (policy is not null) Marshal.FinalReleaseComObject(policy); }
        return created;
    }

    public IReadOnlyList<FirewallRuleInfo> List()
    {
        dynamic policy = CreatePolicy();
        var list = new List<FirewallRuleInfo>();
        try
        {
            foreach (dynamic rule in policy.Rules)
            {
                string name = rule.Name ?? "";
                string? grouping = rule.Grouping;
                if (grouping == Group && name.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    string addresses = rule.RemoteAddresses ?? "";
                    list.Add(new FirewallRuleInfo(
                        name,
                        ServerCodeOf(name),
                        (int)rule.Direction == DirectionIn ? "in" : "out",
                        addresses.Length == 0 ? 0 : addresses.Split(',').Length,
                        (string?)rule.ApplicationName ?? "",
                        (bool)rule.Enabled));
                }
                Marshal.FinalReleaseComObject(rule);
            }
        }
        finally { Marshal.FinalReleaseComObject(policy); }
        return list;
    }

    /// <summary>The server codes currently blocked by our rules.</summary>
    public IReadOnlySet<string> BlockedServers() =>
        List().Select(r => r.ServerCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public int RemoveAll()
    {
        EnsureElevated();
        var names = List().Select(r => r.Name).Distinct().ToList();
        dynamic policy = CreatePolicy();
        try
        {
            // Rules.Remove deletes one rule per call even when several share a name, so loop until gone.
            foreach (string name in names)
                for (int guard = 0; guard < 16 && RuleExists(policy, name); guard++)
                    policy.Rules.Remove(name);
        }
        finally { Marshal.FinalReleaseComObject(policy); }
        return names.Count;
    }

    private static bool RuleExists(dynamic policy, string name)
    {
        try { var r = policy.Rules.Item(name); Marshal.FinalReleaseComObject(r); return true; }
        catch (COMException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static dynamic CreatePolicy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    private static void EnsureElevated()
    {
        if (!IsElevated())
            throw new UnauthorizedAccessException("Changing firewall rules needs administrator rights. Run the app as administrator.");
    }
}
