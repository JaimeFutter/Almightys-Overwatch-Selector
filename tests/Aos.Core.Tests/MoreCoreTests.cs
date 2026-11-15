using System.Net;
using System.Text;
using Aos.Core.Catalog;
using Aos.Core.Firewall;
using Aos.Core.Game;
using Aos.Core.Learn;
using Aos.Core.Net;

namespace Aos.Core.Tests;

public class CidrPropertyTests
{
    /// <summary>
    /// Randomised check: merging must cover exactly the same addresses as the input,
    /// produce no overlaps, and never produce more blocks than went in.
    /// </summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(42)] [InlineData(1337)]
    public void Merge_preserves_the_exact_address_set(int seed)
    {
        var rng = new Random(seed);
        var input = Enumerable.Range(0, 60).Select(_ =>
        {
            int prefix = rng.Next(22, 33); // inside 10.0.0.0/16 so we can enumerate every address
            uint addr = 0x0A000000u | (uint)rng.Next(0, 65536);
            return CidrBlock.Create(new IPAddress(BitConverter.GetBytes(addr).Reverse().ToArray()), prefix);
        }).ToList();

        var merged = CidrBlock.Merge(input);

        static HashSet<uint> Expand(IEnumerable<CidrBlock> blocks)
        {
            var set = new HashSet<uint>();
            foreach (var b in blocks)
                for (UInt128 a = b.First; a <= b.Last; a++) set.Add((uint)a);
            return set;
        }

        Assert.True(Expand(input).SetEquals(Expand(merged)));
        Assert.True(merged.Count <= input.Count);
        for (int i = 1; i < merged.Count; i++)
        {
            Assert.False(merged[i - 1].Overlaps(merged[i]));
            Assert.True(merged[i - 1].Last < merged[i].First);
        }
    }

    [Theory]
    [InlineData("not an ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("2600::/129")]
    [InlineData("10.0.0.0/abc")]
    public void TryParse_rejects_garbage(string text) => Assert.False(CidrBlock.TryParse(text, out _));

    [Fact]
    public void Ranges_reject_mixed_families_and_accept_reversed_bounds()
    {
        Assert.Throws<FormatException>(() => CidrBlock.ParseMany("10.0.0.0-2600::1"));
        Assert.Equal(["10.0.0.0/30"], CidrBlock.ParseMany("10.0.0.3-10.0.0.0").Select(b => b.ToString()));
    }

    [Fact]
    public void Ipv6_merge_and_containment()
    {
        string[] input = ["2600:1900:4000::/45", "2600:1900:4008::/45", "2600:1900:4000:1::/64"];
        var merged = CidrBlock.Merge(input.Select(CidrBlock.Parse));
        Assert.Equal(["2600:1900:4000::/44"], merged.Select(b => b.ToString()));
        Assert.True(merged[0].Contains(IPAddress.Parse("2600:1900:400f:ffff::1")));
        Assert.False(merged[0].Contains(IPAddress.Parse("2600:1900:4010::1")));
    }

    [Fact]
    public void Block_containment_and_overlap()
    {
        var big = CidrBlock.Parse("64.224.0.0/21");
        Assert.True(big.Contains(CidrBlock.Parse("64.224.4.0/24")));
        Assert.False(big.Contains(CidrBlock.Parse("64.224.0.0/20")));
        Assert.True(big.Overlaps(CidrBlock.Parse("64.224.0.0/20")));
        Assert.False(big.Overlaps(CidrBlock.Parse("64.224.8.0/24")));
        Assert.False(big.Overlaps(CidrBlock.Parse("2600::/16")));
    }

    [Fact]
    public void Merge_of_empty_input_is_empty() => Assert.Empty(CidrBlock.Merge([]));
}

public class CatalogTests
{
    private static Stream Json(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Duplicate_codes_are_rejected()
    {
        const string json = """{ "servers": [ { "code": "a", "name": "A", "region": "EU" }, { "code": "A", "name": "B", "region": "EU" } ] }""";
        var e = Assert.Throws<InvalidDataException>(() => ServerCatalog.Parse(Json(json), "test"));
        Assert.Contains("a", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_user_override_in_the_data_folder_wins_over_the_embedded_catalog()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"aos-cat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ServerCatalog.FileName),
                """{ "servers": [ { "code": "x1", "name": "Test", "region": "ME", "extra": ["203.0.113.0/24"] } ], }""");
            var catalog = ServerCatalog.Load(dir);
            Assert.Equal("x1", Assert.Single(catalog.Servers).Code);
            Assert.EndsWith(ServerCatalog.FileName, catalog.Origin);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Every_embedded_server_has_a_region_and_known_region_name()
    {
        var known = new HashSet<string> { "NA", "EU", "ME", "Asia", "Oceania", "South America" };
        Assert.All(ServerCatalog.LoadEmbedded().Servers, s => Assert.Contains(s.Region, known));
    }

    [Fact]
    public void Find_is_case_insensitive() => Assert.Equal("mes1", ServerCatalog.LoadEmbedded().Find("MES1")!.Code);
}

public class TrafficAnalyzerTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static void Burst(TrafficAnalyzer a, string ip, int packets, double seconds, DateTime start)
    {
        for (int i = 0; i < packets; i++)
            a.Add(start + TimeSpan.FromSeconds(seconds * i / packets), IPAddress.Parse(ip), 100);
    }

    [Fact]
    public void Busiest_destination_above_threshold_is_the_match_server()
    {
        var a = new TrafficAnalyzer(TimeSpan.FromSeconds(5), threshold: 20);
        Burst(a, "34.166.0.84", 300, 5, T0);   // 60 pkt/s: game server
        Burst(a, "24.105.30.129", 20, 5, T0);  // 4 pkt/s: Battle.net chatter

        var top = a.Top(T0.AddSeconds(5));
        Assert.Equal("34.166.0.84", top[0].Address.ToString());
        Assert.Equal(60, top[0].PacketsPerSecond);
        Assert.True(a.Evaluate(top, out var server));
        Assert.Equal("34.166.0.84", server!.Address.ToString());
        Assert.False(a.Evaluate(a.Top(T0.AddSeconds(5)), out _)); // unchanged: no second event
    }

    [Fact]
    public void Light_traffic_never_counts_as_a_match()
    {
        var a = new TrafficAnalyzer(TimeSpan.FromSeconds(5), threshold: 20);
        Burst(a, "34.166.0.84", 50, 5, T0); // 10 pkt/s
        Assert.False(a.Evaluate(a.Top(T0.AddSeconds(5)), out _));
        Assert.Null(a.Current);
    }

    [Fact]
    public void Match_ends_when_traffic_ages_out_of_the_window()
    {
        var a = new TrafficAnalyzer(TimeSpan.FromSeconds(5), threshold: 20);
        Burst(a, "34.166.0.84", 300, 5, T0);
        a.Evaluate(a.Top(T0.AddSeconds(5)), out _);

        Assert.True(a.Evaluate(a.Top(T0.AddSeconds(11)), out var after));
        Assert.Null(after);
        Assert.Null(a.Current);
    }

    [Fact]
    public void Switching_servers_raises_a_change()
    {
        var a = new TrafficAnalyzer(TimeSpan.FromSeconds(5), threshold: 20);
        Burst(a, "34.166.0.84", 300, 5, T0);
        a.Evaluate(a.Top(T0.AddSeconds(5)), out _);
        Burst(a, "64.224.26.10", 300, 5, T0.AddSeconds(20));
        Assert.True(a.Evaluate(a.Top(T0.AddSeconds(25)), out var next));
        Assert.Equal("64.224.26.10", next!.Address.ToString());
    }
}

public class LearnedStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aos-learned-{Guid.NewGuid():N}.json");
    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Ipv6_servers_widen_to_a_slash_64()
    {
        var store = new LearnedStore(_path);
        var item = store.Record(IPAddress.Parse("2600:1900:5400:12::7"), "gmec1");
        Assert.Equal("2600:1900:5400:12::/64", item.Block.ToString());
    }

    [Fact]
    public void Automatic_classification_updates_until_the_user_assigns()
    {
        var store = new LearnedStore(_path);
        var ip = IPAddress.Parse("203.0.113.9");
        store.Record(ip, null);
        Assert.Equal("ams1", store.Record(ip, "ams1").ServerCode); // auto may fill in a guess
        store.Assign("203.0.113.9", "gmec2");
        Assert.Equal("gmec2", store.Record(ip, "ams1").ServerCode); // user choice sticks
        Assert.Equal(3, store.Items.Single().Sightings);

        store.Assign("203.0.113.9", null); // un-assign hands control back to auto
        Assert.Equal("ams1", store.Record(ip, "ams1").ServerCode);
    }

    [Fact]
    public void Remove_forgets_the_ip_and_persists()
    {
        var store = new LearnedStore(_path);
        store.Record(IPAddress.Parse("203.0.113.9"), "gmec1");
        store.Remove("203.0.113.9");
        Assert.Empty(new LearnedStore(_path).Items);
        Assert.Empty(store.BlocksFor("gmec1"));
    }

    [Fact]
    public void Assigning_an_unknown_ip_throws() =>
        Assert.Throws<KeyNotFoundException>(() => new LearnedStore(_path).Assign("198.51.100.1", "gmec1"));
}

public class GameAndFirewallNamingTests
{
    [Fact]
    public void Steam_library_paths_are_read_from_libraryfolders_vdf()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"		"C:\\Program Files (x86)\\Steam"  "apps" { "228980" "1" } }
                "1" { "path"		"D:\\SteamLibrary"  "apps" { "2357570" "1" } }
                "2" { "path"		"d:\\steamlibrary" }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameLocator.ParseSteamLibraries(vdf));
    }

    [Theory]
    [InlineData("AOS - MES1 - 1 out", "mes1")]
    [InlineData("AOS - GMEC2 - 12 in", "gmec2")]
    [InlineData("Overwatch Application", "")]
    [InlineData("AOS | MES1 | 1 out", "")] // old pipe format isn't ours anymore
    public void Server_code_is_read_back_from_rule_names(string name, string expected) =>
        Assert.Equal(expected, FirewallService.ServerCodeOf(name));

    [Fact]
    public void Every_catalog_server_produces_a_valid_rule_name()
    {
        foreach (var s in ServerCatalog.LoadEmbedded().Servers)
        {
            string name = FirewallService.RuleName(s.Code, 0, "out");
            Assert.DoesNotContain('|', name);
            Assert.Equal(s.Code, FirewallService.ServerCodeOf(name));
        }
    }

    [Fact]
    public void Chunking_an_empty_list_creates_no_rules() =>
        Assert.Empty(FirewallService.Chunk([]));
}
