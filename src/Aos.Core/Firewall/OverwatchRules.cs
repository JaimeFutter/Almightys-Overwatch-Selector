using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Aos.Core.Firewall;

public enum RuleSource
{
    /// <summary>Created by this app.</summary>
    Ours,
    /// <summary>Created by dropship (any version).</summary>
    Dropship,
    /// <summary>Windows' own "allow this app" rule, created when Overwatch first asked through the firewall prompt.</summary>
    WindowsPrompt,
    /// <summary>Anything else that targets Overwatch: other selector tools or rules made by hand.</summary>
    Other,
}

/// <summary>A Windows Firewall rule that affects Overwatch, whoever created it.</summary>
public sealed record OverwatchRule
{
    /// <summary>Stable fingerprint of the rule's settings. Rule names aren't unique, so this is how a rule is addressed.</summary>
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Grouping { get; init; } = "";
    public string Description { get; init; } = "";
    public string Program { get; init; } = "";
    public bool IsBlock { get; init; }
    public bool Inbound { get; init; }
    public bool Enabled { get; init; }
    public int Protocol { get; init; }
    public int AddressCount { get; init; }
    public RuleSource Source { get; init; }

    public string SourceText => Source switch
    {
        RuleSource.Ours => "This app",
        RuleSource.Dropship => "dropship",
        RuleSource.WindowsPrompt => "Windows (allow prompt)",
        _ => "Other / manual",
    };

    public string ActionText => IsBlock ? "Block" : "Allow";
    public string DirectionText => Inbound ? "In" : "Out";

    /// <summary>One-line summary for lists: "Block · Inbound · 109 addresses · group · Overwatch.exe".</summary>
    public string DetailText
    {
        get
        {
            var parts = new List<string> { $"{ActionText} · {(Inbound ? "Inbound" : "Outbound")}" };
            parts.Add(AddressCount == 0 ? "any address" : $"{AddressCount} addresses");
            if (!string.IsNullOrEmpty(Grouping)) parts.Add(Grouping);
            if (!string.IsNullOrEmpty(Program)) parts.Add(Path.GetFileName(Program));
            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>Finds, toggles and deletes every firewall rule that targets Overwatch, not just ours.</summary>
public static class OverwatchRules
{
    /// <summary>Groups dropship has used for its firewall rules across versions.</summary>
    public static readonly string[] DropshipGroups = ["stormy/dropship/v3", "stormy/dropship", "stormy.gg/dropship"];

    public static bool IsRelated(string? name, string? grouping, string? program) =>
        IsOverwatchProgram(program)
        || Contains(name, "overwatch") || Contains(grouping, "overwatch")
        || IsDropship(grouping, name)
        || (grouping == FirewallService.Group);

    public static RuleSource Classify(string? name, string? grouping, string? description, bool isBlock)
    {
        if (grouping == FirewallService.Group) return RuleSource.Ours;
        if (IsDropship(grouping, name) || Contains(description, "dropship")) return RuleSource.Dropship;
        // Windows names its prompt rules after the app ("Overwatch Application") with no group and allow action.
        if (!isBlock && string.IsNullOrEmpty(grouping)) return RuleSource.WindowsPrompt;
        return RuleSource.Other;
    }

    public static IReadOnlyList<OverwatchRule> List()
    {
        var list = new List<OverwatchRule>();
        ForEachRule((rule, info) => { if (info is not null) list.Add(info); return false; });
        return list;
    }

    /// <summary>Enables or disables the rules with these keys. Returns how many changed.</summary>
    public static int SetEnabled(IEnumerable<string> keys, bool enabled)
    {
        RequireAdmin();
        var wanted = keys.ToHashSet();
        int changed = 0;
        ForEachRule((rule, info) =>
        {
            if (info is not null && wanted.Contains(info.Key) && info.Enabled != enabled)
            {
                rule.Enabled = enabled;
                changed++;
            }
            return false;
        });
        return changed;
    }

    /// <summary>
    /// Deletes the rules with these keys. INetFwRules.Remove only takes a name, and names repeat
    /// ("Overwatch Application" twice), so each target is first renamed to a unique name and then removed.
    /// </summary>
    public static int Remove(IEnumerable<string> keys)
    {
        RequireAdmin();
        var wanted = keys.ToHashSet();
        var tempNames = new List<string>();
        ForEachRule((rule, info) =>
        {
            if (info is not null && wanted.Remove(info.Key))
            {
                string temp = $"AOS-delete-{Guid.NewGuid():N}";
                rule.Name = temp;
                tempNames.Add(temp);
            }
            return false;
        });

        dynamic policy = CreatePolicy();
        try { foreach (string n in tempNames) policy.Rules.Remove(n); }
        finally { Marshal.FinalReleaseComObject(policy); }
        return tempNames.Count;
    }

    /// <summary>Deletes every rule that blocks Overwatch (ours, dropship's, anyone's). Allow rules are kept.</summary>
    public static int RemoveAllBlocks() => Remove(List().Where(r => r.IsBlock).Select(r => r.Key));

    // ----- helpers -----

    private static void ForEachRule(Func<dynamic, OverwatchRule?, bool> visit)
    {
        dynamic policy = CreatePolicy();
        try
        {
            foreach (dynamic rule in policy.Rules)
            {
                try
                {
                    string name = rule.Name ?? "";
                    string grouping = rule.Grouping ?? "";
                    string program = rule.ApplicationName ?? "";
                    OverwatchRule? info = null;
                    if (IsRelated(name, grouping, program))
                    {
                        string description = rule.Description ?? "";
                        string addresses = rule.RemoteAddresses ?? "*";
                        bool isBlock = (int)rule.Action == 0;
                        bool inbound = (int)rule.Direction == 1;
                        int protocol = (int)rule.Protocol;
                        info = new OverwatchRule
                        {
                            Key = Fingerprint(name, grouping, program, isBlock, inbound, protocol, addresses, (string?)rule.LocalPorts ?? ""),
                            Name = name,
                            Grouping = grouping,
                            Description = description,
                            Program = program,
                            IsBlock = isBlock,
                            Inbound = inbound,
                            Enabled = (bool)rule.Enabled,
                            Protocol = protocol,
                            AddressCount = addresses is "*" or "" ? 0 : addresses.Split(',').Length,
                            Source = Classify(name, grouping, description, isBlock),
                        };
                    }
                    if (visit(rule, info)) break;
                }
                finally { Marshal.FinalReleaseComObject(rule); }
            }
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }

    internal static string Fingerprint(params object[] parts)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", parts)));
        return Convert.ToHexString(hash, 0, 12);
    }

    private static bool IsOverwatchProgram(string? path) =>
        !string.IsNullOrEmpty(path) &&
        Path.GetFileName(path).Equals("Overwatch.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsDropship(string? grouping, string? name) =>
        DropshipGroups.Any(g => string.Equals(grouping, g, StringComparison.OrdinalIgnoreCase)) || Contains(name, "dropship");

    private static bool Contains(string? text, string value) =>
        text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static dynamic CreatePolicy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    private static void RequireAdmin()
    {
        if (!FirewallService.IsElevated())
            throw new UnauthorizedAccessException("Changing firewall rules needs administrator rights. Run the app as administrator.");
    }
}
