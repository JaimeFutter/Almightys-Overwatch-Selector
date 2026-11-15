using System.Net;
using System.Text;
using Aos.Core.Catalog;
using Aos.Core.Firewall;
using Aos.Core.Learn;
using Aos.Core.Net;
using Aos.Core.Sources;

namespace Aos.Core.Tests;

public class SourceAndBuilderTests
{
    private static Stream Json(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private const string GcpJson = """
        { "syncToken": "1", "prefixes": [
          { "ipv4Prefix": "34.166.0.0/16", "service": "Google Cloud", "scope": "me-central2" },
          { "ipv4Prefix": "34.18.0.0/16",  "service": "Google Cloud", "scope": "me-central1" },
          { "ipv6Prefix": "2600:1900:5400::/44", "service": "Google Cloud", "scope": "me-central1" },
          { "ipv4Prefix": "34.165.0.0/16", "service": "Google Cloud", "scope": "me-west1" },
          { "ipv4Prefix": "34.88.0.0/16",  "service": "Google Cloud", "scope": "europe-north1" }
        ] }
        """;

    private const string AwsJson = """
        { "prefixes": [
            { "ip_prefix": "157.175.0.0/16", "region": "me-south-1", "service": "EC2" },
            { "ip_prefix": "157.175.0.0/16", "region": "me-south-1", "service": "AMAZON" },
            { "ip_prefix": "3.28.0.0/15",    "region": "me-central-1", "service": "EC2" },
            { "ip_prefix": "51.17.0.0/16",   "region": "me-west-1", "service": "EC2" }
          ],
          "ipv6_prefixes": [ { "ipv6_prefix": "2a05:d01c::/36", "region": "me-south-1", "service": "EC2" } ] }
        """;

    private const string RipeJson = """
        { "data": { "prefixes": [ { "prefix": "64.224.26.0/23" }, { "prefix": "64.224.0.0/21" }, { "prefix": "2a04:e802::/32" } ] } }
        """;

    [Fact]
    public async Task Gcp_parser_groups_by_scope_and_keeps_ipv6()
    {
        var d = await GcpRangeSource.ParseAsync(Json(GcpJson));
        Assert.Equal(2, d["me-central1"].Count);
        Assert.Contains(d["me-central1"], b => b.IsV6);
    }

    [Fact]
    public async Task Aws_parser_keeps_only_ec2()
    {
        var d = await AwsRangeSource.ParseAsync(Json(AwsJson));
        Assert.Equal(2, d["me-south-1"].Count); // one v4 EC2 + one v6 EC2; AMAZON duplicate ignored
    }

    [Fact]
    public async Task Ripe_parser_reads_announced_prefixes()
    {
        var list = await BlizzardAsnSource.ParseAsync(Json(RipeJson));
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Embedded_catalog_is_valid_and_covers_middle_east_from_every_provider()
    {
        var catalog = ServerCatalog.LoadEmbedded();
        Assert.Equal(catalog.Servers.Count, catalog.Servers.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog.Servers, s => Assert.True(s.Blizzard.Count + s.Gcp.Count + s.Aws.Count + s.Extra.Count > 0, s.Code));
        Assert.All(catalog.Servers.SelectMany(s => s.Blizzard), c => Assert.True(CidrBlock.TryParse(c, out _), c));

        var me = catalog.Servers.Where(s => s.Region == "ME").ToList();
        var gcp = me.SelectMany(s => s.Gcp).ToHashSet();
        var aws = me.SelectMany(s => s.Aws).ToHashSet();
        Assert.Superset(new HashSet<string> { "me-central1", "me-central2", "me-west1" }, gcp);
        Assert.Superset(new HashSet<string> { "me-south-1", "me-central-1" }, aws);
    }

    private static async Task<SourceSnapshot> SnapshotAsync()
    {
        var gcp = await GcpRangeSource.ParseAsync(Json(GcpJson));
        var aws = await AwsRangeSource.ParseAsync(Json(AwsJson));
        var asn = await BlizzardAsnSource.ParseAsync(Json(RipeJson));
        return new SourceSnapshot
        {
            GcpFetched = DateTimeOffset.Now, AwsFetched = DateTimeOffset.Now, AsnFetched = DateTimeOffset.Now,
            Gcp = gcp.ToDictionary(k => k.Key, v => v.Value.Select(b => b.ToString()).ToList()),
            Aws = aws.ToDictionary(k => k.Key, v => v.Value.Select(b => b.ToString()).ToList()),
            BlizzardAsn = asn.Select(b => b.ToString()).ToList(),
        };
    }

    [Fact]
    public async Task Builder_resolves_middle_east_and_classifies_ips()
    {
        var set = RangeBuilder.Build(ServerCatalog.LoadEmbedded(), await SnapshotAsync(), learned: null);

        Assert.Equal("gmec2", set.Classify(IPAddress.Parse("34.166.0.84"))!.Code);
        Assert.Equal("gmec1", set.Classify(IPAddress.Parse("34.18.10.1"))!.Code);
        Assert.Equal("mes1", set.Classify(IPAddress.Parse("157.175.3.4"))!.Code);
        Assert.Equal("mes1", set.Classify(IPAddress.Parse("3.29.1.1"))!.Code);
        Assert.Equal("gmew1", set.Classify(IPAddress.Parse("51.17.0.9"))!.Code);
        Assert.Equal("ams1", set.Classify(IPAddress.Parse("64.224.27.1"))!.Code);
        // Seen with Learn mode during a real match; the in-game network graph showed AMS1.
        Assert.Equal("ams1", set.Classify(IPAddress.Parse("137.221.79.101"))!.Code);
        Assert.Null(set.Classify(IPAddress.Parse("1.1.1.1")));

        Assert.Equal("Google Cloud me-central2", set.DescribeOwner(IPAddress.Parse("34.166.0.84")));
        Assert.Equal("Blizzard network (AS57976)", set.DescribeOwner(IPAddress.Parse("64.224.26.5")));
    }

    [Fact]
    public async Task Builder_warns_about_seed_ranges_blizzard_no_longer_announces()
    {
        var set = RangeBuilder.Build(ServerCatalog.LoadEmbedded(), await SnapshotAsync(), learned: null);
        Assert.DoesNotContain(set.For("ams1")!.Warnings, w => w.Contains("64.224.26.0/23")); // announced in the fixture
        Assert.Contains(set.For("las1")!.Warnings, w => w.Contains("64.224.24.0/23"));
    }

    [Fact]
    public async Task Learned_ips_are_added_as_a_slash_24_to_their_server()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aos-learned-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LearnedStore(path);
            store.Record(IPAddress.Parse("203.0.113.77"), null);
            store.Assign("203.0.113.77", "gmec1");
            store.Record(IPAddress.Parse("203.0.113.77"), "ams1"); // auto-classification must not override the user

            var reloaded = new LearnedStore(path);
            Assert.Equal("gmec1", reloaded.Items.Single().ServerCode);

            var set = RangeBuilder.Build(ServerCatalog.LoadEmbedded(), await SnapshotAsync(), reloaded);
            Assert.Equal("gmec1", set.Classify(IPAddress.Parse("203.0.113.1"))!.Code);
            Assert.Equal(1, set.For("gmec1")!.LearnedCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Firewall_chunks_large_lists()
    {
        var blocks = Enumerable.Range(0, 2500).Select(i => CidrBlock.Parse($"10.{i / 256}.{i % 256}.0/24"));
        var chunks = FirewallService.Chunk(blocks);
        Assert.Equal([1000, 1000, 500], chunks.Select(c => c.Count));
        string name = FirewallService.RuleName("gmec1", 1, "out");
        Assert.Equal("AOS - GMEC1 - 2 out", name);
        Assert.DoesNotContain('|', name); // Windows rejects rules containing '|'
        Assert.Equal("gmec1", FirewallService.ServerCodeOf(name));
    }

    [Theory]
    [InlineData("192.168.1.10", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("239.255.255.250", false)]
    [InlineData("34.166.0.84", true)]
    [InlineData("fe80::1", false)]
    [InlineData("2600:1900::1", true)]
    public void Observer_ignores_private_addresses(string ip, bool expected) =>
        Assert.Equal(expected, ServerObserver.IsPublic(IPAddress.Parse(ip)));
}
