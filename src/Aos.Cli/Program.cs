using System.Net;
using Aos.Core;
using Aos.Core.Catalog;
using Aos.Core.Firewall;
using Aos.Core.Game;
using Aos.Core.Learn;
using Aos.Core.Net;
using Aos.Core.Ping;

// aos — headless companion to the app, for checking IP lists and rules.
//   aos dump [server...] [--refresh] [--full]   resolved ranges per server
//   aos classify <ip>                           which server/network owns an IP
//   aos ping                                    latency to each server's ping host
//   aos apply --allow <code|region>... [--game <path>]   block everything else (admin)
//   aos block <code|region>...  [--game <path>] block only these (admin)
//   aos status                                  list our firewall rules
//   aos clear                                   remove our firewall rules (admin)
//   aos learn                                   watch Overwatch traffic for the match server (admin)

var argv = args.ToList();
string cmd = argv.Count > 0 ? argv[0].ToLowerInvariant() : "help";
var rest = argv.Skip(1).ToList();
bool Flag(string f) => rest.Remove(f);
string? Option(string name)
{
    int i = rest.IndexOf(name);
    if (i < 0 || i + 1 >= rest.Count) return null;
    string v = rest[i + 1];
    rest.RemoveRange(i, 2);
    return v;
}

var catalog = ServerCatalog.Load();
var learned = new LearnedStore();
var builder = new RangeBuilder();

try
{
    switch (cmd)
    {
        case "dump": await Dump(); break;
        case "classify": await Classify(); break;
        case "ping": await PingAll(); break;
        case "apply": await Apply(allowMode: true); break;
        case "block": await Apply(allowMode: false); break;
        case "status": Status(); break;
        case "clear": Console.WriteLine($"Removed {new FirewallService().RemoveAll()} rule name(s)."); break;
        case "learn": await Learn(); break;
        default: Help(); break;
    }
    return 0;
}
catch (Exception e) when (e is UnauthorizedAccessException or FileNotFoundException or ArgumentException or FormatException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

async Task Dump()
{
    bool refresh = Flag("--refresh"), full = Flag("--full");
    var set = await builder.GetAsync(catalog, learned, refresh);
    foreach (var m in set.Messages) Console.WriteLine($"! {m}");
    var s = set.Snapshot;
    Console.WriteLine($"Sources: GCP {s.GcpFetched:g} ({s.Gcp.Count} regions), AWS {s.AwsFetched:g} ({s.Aws.Count} regions), " +
                      $"Blizzard ASN {s.AsnFetched:g} ({s.BlizzardAsn.Count} prefixes). Catalog: {catalog.Origin}");
    Console.WriteLine();

    var codes = rest.Count > 0 ? Resolve(rest) : catalog.Servers.Select(x => x.Code).ToHashSet();
    foreach (var region in catalog.Servers.Where(x => codes.Contains(x.Code)).GroupBy(x => x.Region))
    {
        Console.WriteLine($"== {region.Key} ==");
        foreach (var server in region)
        {
            var r = set.For(server.Code)!;
            int v4 = r.Blocks.Count(b => !b.IsV6), v6 = r.Blocks.Count(b => b.IsV6);
            Console.WriteLine($"  {server.DisplayName,-34} {v4,5} v4 {v6,5} v6   blizzard {r.BlizzardCount}, gcp {r.GcpCount}, aws {r.AwsCount}, learned {r.LearnedCount}" +
                              (server.Experimental ? "  [experimental]" : ""));
            foreach (var w in r.Warnings) Console.WriteLine($"      ! {w}");
            if (full) foreach (var b in r.Blocks) Console.WriteLine($"      {b}");
        }
    }
}

async Task Classify()
{
    if (rest.Count == 0) throw new ArgumentException("usage: aos classify <ip>");
    var set = await builder.GetAsync(catalog, learned);
    foreach (string text in rest)
    {
        var ip = IPAddress.Parse(text);
        var server = set.Classify(ip);
        Console.WriteLine($"{ip,-40} {server?.DisplayName ?? "no catalog server",-34} [{server?.Region ?? "-"}]  {set.DescribeOwner(ip)}");
    }
}

async Task PingAll()
{
    var tasks = catalog.Servers.Where(s => s.Ping is not null)
        .Select(async s => (s, ms: await LatencyProbe.MeasureAsync(s.Ping!))).ToList();
    foreach (var (s, ms) in (await Task.WhenAll(tasks)).OrderBy(x => x.ms ?? int.MaxValue))
        Console.WriteLine($"  {s.DisplayName,-34} {(ms is null ? "no reply" : $"{ms} ms")}");
}

async Task Apply(bool allowMode)
{
    string? game = GameLocator.Find(Option("--game"))
        ?? throw new FileNotFoundException("Couldn't find Overwatch.exe. Pass --game \"C:\\path\\to\\Overwatch.exe\".");
    if (rest.Count == 0) throw new ArgumentException($"usage: aos {(allowMode ? "apply --allow" : "block")} <code|region>...");
    rest.Remove("--allow");
    var chosen = Resolve(rest);
    var set = await builder.GetAsync(catalog, learned);
    var blocked = catalog.Servers
        .Where(s => allowMode ? !chosen.Contains(s.Code) : chosen.Contains(s.Code))
        .Select(s => (s.Code, (IReadOnlyList<CidrBlock>)set.For(s.Code)!.Blocks))
        .Where(x => x.Item2.Count > 0)
        .ToList();
    int n = new FirewallService().Apply(game, blocked);
    Console.WriteLine($"Game: {game}");
    Console.WriteLine($"Blocked {blocked.Count} server(s) with {n} rule(s): {string.Join(", ", blocked.Select(b => b.Code.ToUpperInvariant()))}");
}

void Status()
{
    var rules = new FirewallService().List();
    if (rules.Count == 0) { Console.WriteLine("No rules. Overwatch can reach every server."); return; }
    foreach (var g in rules.GroupBy(r => r.ServerCode))
        Console.WriteLine($"  {g.Key.ToUpperInvariant(),-6} {g.Count()} rule(s), {g.Where(r => r.Direction == "out").Sum(r => r.AddressCount)} addresses -> {g.First().Program}");
}

async Task Learn()
{
    var set = await builder.GetAsync(catalog, learned);
    using var observer = new ServerObserver();
    observer.Error += e => Console.Error.WriteLine($"etw: {e}");
    observer.ServerChanged += s =>
    {
        if (s is null) { Console.WriteLine($"[{DateTime.Now:T}] match ended / no server traffic"); return; }
        var server = set.Classify(s.Address);
        var item = learned.Record(s.Address, server?.Code);
        Console.WriteLine($"[{DateTime.Now:T}] server {s.Address} ~{s.PacketsPerSecond} pkt/s -> {server?.DisplayName ?? "UNKNOWN"} ({set.DescribeOwner(s.Address)}), seen {item.Sightings}x");
    };
    observer.Start();
    Console.WriteLine("Watching Overwatch.exe traffic. Start a match; Ctrl+C to stop.");
    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    await done.Task;
}

HashSet<string> Resolve(IEnumerable<string> tokens)
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string t in tokens)
    {
        var byRegion = catalog.Servers.Where(s => s.Region.Equals(t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byRegion.Count > 0) { foreach (var s in byRegion) set.Add(s.Code); continue; }
        var server = catalog.Find(t) ?? throw new ArgumentException($"Unknown server or region '{t}'. Regions: {string.Join(", ", catalog.Regions)}");
        set.Add(server.Code);
    }
    return set;
}

void Help() => Console.WriteLine("""
    aos dump [server|region...] [--refresh] [--full]
    aos classify <ip>...
    aos ping
    aos apply --allow <server|region>... [--game <path>]   (admin) block every other server
    aos block <server|region>... [--game <path>]           (admin) block only these
    aos status
    aos clear                                              (admin)
    aos learn                                              (admin) detect the match server
    """);
