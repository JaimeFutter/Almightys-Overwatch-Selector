using System.Net;
using System.Text.Json;
using Aos.Core.Catalog;
using Aos.Core.Learn;
using Aos.Core.Net;
using Aos.Core.Sources;

namespace Aos.Core;

/// <summary>Raw data downloaded from the public sources, cached so the app works offline.</summary>
public sealed record SourceSnapshot
{
    public DateTimeOffset? GcpFetched { get; set; }
    public Dictionary<string, List<string>> Gcp { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? AwsFetched { get; set; }
    public Dictionary<string, List<string>> Aws { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? AsnFetched { get; set; }
    public List<string> BlizzardAsn { get; set; } = [];
}

public sealed record ServerRanges
{
    public required ServerDefinition Server { get; init; }
    public required List<CidrBlock> Blocks { get; init; }
    public int BlizzardCount { get; init; }
    public int GcpCount { get; init; }
    public int AwsCount { get; init; }
    public int LearnedCount { get; init; }
    public List<string> Warnings { get; init; } = [];
}

/// <summary>The resolved block lists for every server, plus lookups used to classify an IP.</summary>
public sealed class RangeSet
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyDictionary<string, ServerRanges> Servers { get; init; }
    public required SourceSnapshot Snapshot { get; init; }
    public List<string> Messages { get; init; } = [];

    private List<CidrBlock>? _asn;

    public ServerRanges? For(string code) => Servers.TryGetValue(code, out var r) ? r : null;

    /// <summary>Finds the server whose ranges contain this IP (most specific match wins).</summary>
    public ServerDefinition? Classify(IPAddress ip)
    {
        ServerDefinition? best = null;
        int bestPrefix = -1;
        foreach (var r in Servers.Values)
            foreach (var b in r.Blocks)
                if (b.PrefixLength > bestPrefix && b.Contains(ip)) { best = r.Server; bestPrefix = b.PrefixLength; }
        return best;
    }

    /// <summary>Describes who owns an IP: Blizzard's network, a Google Cloud region, or an AWS region.</summary>
    public string DescribeOwner(IPAddress ip)
    {
        _asn ??= Snapshot.BlizzardAsn.Select(CidrBlock.Parse).ToList();
        if (_asn.Any(b => b.Contains(ip))) return "Blizzard network (AS57976)";
        foreach (var (scope, list) in Snapshot.Gcp)
            if (list.Any(p => CidrBlock.Parse(p).Contains(ip))) return $"Google Cloud {scope}";
        foreach (var (region, list) in Snapshot.Aws)
            if (list.Any(p => CidrBlock.Parse(p).Contains(ip))) return $"AWS {region}";
        return "Unknown network";
    }
}

public sealed class RangeBuilder
{
    public const string SnapshotFile = "sources-cache.json";
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly HttpClient _http;

    /// <summary>The shared HTTP client (also used for update checks).</summary>
    public HttpClient Http => _http;
    private readonly string _snapshotPath;

    public RangeBuilder(HttpClient? http = null, string? snapshotPath = null)
    {
        _http = http ?? CreateHttpClient();
        _snapshotPath = snapshotPath ?? AppPaths.File(SnapshotFile);
    }

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AlmightysOverwatchSelector/1.0");
        return http;
    }

    public SourceSnapshot LoadSnapshot()
    {
        if (!File.Exists(_snapshotPath)) return new SourceSnapshot();
        try { return JsonSerializer.Deserialize<SourceSnapshot>(File.ReadAllText(_snapshotPath), ServerCatalog.JsonOptions) ?? new(); }
        catch (JsonException) { return new SourceSnapshot(); }
    }

    public bool IsStale(SourceSnapshot s) =>
        s.GcpFetched is null || s.AwsFetched is null || s.AsnFetched is null ||
        DateTimeOffset.Now - new[] { s.GcpFetched.Value, s.AwsFetched.Value, s.AsnFetched.Value }.Min() > MaxAge;

    /// <summary>
    /// Downloads every source. A source that fails keeps its previously cached copy, and a message says so.
    /// </summary>
    public async Task<(SourceSnapshot Snapshot, List<string> Messages)> RefreshAsync(CancellationToken ct = default)
    {
        var snap = LoadSnapshot();
        var messages = new List<string>();

        var gcp = Try(() => GcpRangeSource.FetchAsync(_http, ct), "Google Cloud", messages);
        var aws = Try(() => AwsRangeSource.FetchAsync(_http, ct), "AWS", messages);
        var asn = Try(() => BlizzardAsnSource.FetchAsync(_http, ct), "Blizzard ASN (RIPEstat)", messages);
        await Task.WhenAll(gcp, aws, asn);

        var now = DateTimeOffset.Now;
        if (gcp.Result is { } g) { snap.Gcp = ToStrings(g); snap.GcpFetched = now; }
        if (aws.Result is { } a) { snap.Aws = ToStrings(a); snap.AwsFetched = now; }
        if (asn.Result is { } b) { snap.BlizzardAsn = b.Select(x => x.ToString()).ToList(); snap.AsnFetched = now; }

        Directory.CreateDirectory(Path.GetDirectoryName(_snapshotPath)!);
        File.WriteAllText(_snapshotPath, JsonSerializer.Serialize(snap, ServerCatalog.JsonOptions));
        return (snap, messages);
    }

    /// <summary>Loads cached sources, refreshing them first when forced or older than <see cref="MaxAge"/>.</summary>
    public async Task<RangeSet> GetAsync(ServerCatalog catalog, LearnedStore? learned, bool forceRefresh = false, CancellationToken ct = default)
    {
        var snap = LoadSnapshot();
        var messages = new List<string>();
        if (forceRefresh || IsStale(snap))
            (snap, messages) = await RefreshAsync(ct);
        var set = Build(catalog, snap, learned);
        set.Messages.InsertRange(0, messages);
        return set;
    }

    /// <summary>Pure function: resolves each server's block list from a snapshot (no network).</summary>
    public static RangeSet Build(ServerCatalog catalog, SourceSnapshot snap, LearnedStore? learned)
    {
        var asn = snap.BlizzardAsn.Select(CidrBlock.Parse).ToList();
        var servers = new Dictionary<string, ServerRanges>(StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var s in catalog.Servers)
        {
            var warnings = new List<string>();
            var blocks = new List<CidrBlock>();

            var blizz = s.Blizzard.SelectMany(CidrBlock.ParseMany).ToList();
            if (asn.Count > 0)
                foreach (var b in blizz.Where(b => !asn.Any(a => a.Overlaps(b))))
                    warnings.Add($"{b} is no longer announced by Blizzard's network; it may be stale.");
            blocks.AddRange(blizz);

            int gcpCount = 0;
            foreach (var scope in s.Gcp)
            {
                if (snap.Gcp.TryGetValue(scope, out var list))
                {
                    var parsed = list.Select(CidrBlock.Parse).ToList();
                    blocks.AddRange(parsed);
                    gcpCount += parsed.Count;
                }
                else if (snap.GcpFetched is not null)
                    warnings.Add($"Google Cloud region '{scope}' was not found in Google's range list.");
            }

            int awsCount = 0;
            foreach (var region in s.Aws)
            {
                if (snap.Aws.TryGetValue(region, out var list))
                {
                    var parsed = list.Select(CidrBlock.Parse).ToList();
                    blocks.AddRange(parsed);
                    awsCount += parsed.Count;
                }
                else if (snap.AwsFetched is not null)
                    warnings.Add($"AWS region '{region}' was not found in Amazon's range list.");
            }

            blocks.AddRange(s.Extra.SelectMany(CidrBlock.ParseMany));

            var learnedBlocks = learned?.BlocksFor(s.Code).ToList() ?? [];
            blocks.AddRange(learnedBlocks);

            if (blocks.Count == 0)
                warnings.Add("No IP ranges resolved. Refresh IPs, or this server can't be blocked yet.");

            servers[s.Code] = new ServerRanges
            {
                Server = s,
                Blocks = blocks.Count == 0 ? [] : CidrBlock.Merge(blocks),
                BlizzardCount = blizz.Count,
                GcpCount = gcpCount,
                AwsCount = awsCount,
                LearnedCount = learnedBlocks.Count,
                Warnings = warnings,
            };
        }

        if (snap.GcpFetched is null) messages.Add("Google Cloud ranges have never been downloaded.");
        if (snap.AwsFetched is null) messages.Add("AWS ranges have never been downloaded.");

        return new RangeSet
        {
            GeneratedAt = DateTimeOffset.Now,
            Servers = servers,
            Snapshot = snap,
            Messages = messages,
        };
    }

    private static Dictionary<string, List<string>> ToStrings(Dictionary<string, List<CidrBlock>> d) =>
        d.ToDictionary(kv => kv.Key, kv => kv.Value.Select(b => b.ToString()).ToList(), StringComparer.OrdinalIgnoreCase);

    private static async Task<T?> Try<T>(Func<Task<T>> f, string name, List<string> messages) where T : class
    {
        try { return await f(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            lock (messages) messages.Add($"{name} download failed ({e.Message}); using the cached copy.");
            return null;
        }
    }
}
