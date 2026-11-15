using System.Net;
using System.Text;
using Aos.Core.Catalog;
using Aos.Core.Sources;

namespace Aos.Core.Tests;

/// <summary>Serves canned responses per URL so the download/caching logic runs without the network.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();
    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string url = request.RequestUri!.ToString();
        lock (Requested) Requested.Add(url);
        return Task.FromResult(Routes.TryGetValue(url, out var f) ? f() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public sealed class RangeBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aos-tests-{Guid.NewGuid():N}");
    private string Cache => Path.Combine(_dir, "sources-cache.json");

    private const string Gcp = """{ "prefixes": [ { "ipv4Prefix": "34.166.0.0/16", "scope": "me-central2" }, { "ipv4Prefix": "34.18.0.0/16", "scope": "me-central1" } ] }""";
    private const string Aws = """{ "prefixes": [ { "ip_prefix": "157.175.0.0/16", "region": "me-south-1", "service": "EC2" } ], "ipv6_prefixes": [] }""";
    private const string Ripe = """{ "data": { "prefixes": [ { "prefix": "64.224.26.0/23" } ] } }""";

    public RangeBuilderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static FakeHandler AllOk()
    {
        var h = new FakeHandler();
        h.Routes[GcpRangeSource.Url] = () => FakeHandler.Json(Gcp);
        h.Routes[AwsRangeSource.Url] = () => FakeHandler.Json(Aws);
        h.Routes[BlizzardAsnSource.Url] = () => FakeHandler.Json(Ripe);
        return h;
    }

    [Fact]
    public async Task Refresh_downloads_every_source_and_writes_the_cache()
    {
        var builder = new RangeBuilder(new HttpClient(AllOk()), Cache);
        var (snap, messages) = await builder.RefreshAsync();

        Assert.Empty(messages);
        Assert.NotNull(snap.GcpFetched);
        Assert.Equal(["34.166.0.0/16"], snap.Gcp["me-central2"]);
        Assert.Equal(["157.175.0.0/16"], snap.Aws["me-south-1"]);
        Assert.Equal(["64.224.26.0/23"], snap.BlizzardAsn);
        Assert.True(File.Exists(Cache));

        var reloaded = builder.LoadSnapshot();
        Assert.Equal(snap.Gcp["me-central1"], reloaded.Gcp["me-central1"]);
        Assert.False(builder.IsStale(reloaded));
    }

    [Fact]
    public async Task A_failed_source_keeps_its_cached_copy_and_reports_it()
    {
        var builder = new RangeBuilder(new HttpClient(AllOk()), Cache);
        await builder.RefreshAsync();

        var broken = AllOk();
        broken.Routes[AwsRangeSource.Url] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        broken.Routes[GcpRangeSource.Url] = () => FakeHandler.Json("{ not json");
        var (snap, messages) = await new RangeBuilder(new HttpClient(broken), Cache).RefreshAsync();

        Assert.Contains(messages, m => m.StartsWith("AWS"));
        Assert.Contains(messages, m => m.StartsWith("Google Cloud"));
        Assert.Equal(["157.175.0.0/16"], snap.Aws["me-south-1"]);   // cached copy survived
        Assert.Equal(["34.166.0.0/16"], snap.Gcp["me-central2"]);
    }

    [Fact]
    public async Task GetAsync_only_downloads_when_the_cache_is_missing_or_stale()
    {
        var handler = AllOk();
        var builder = new RangeBuilder(new HttpClient(handler), Cache);
        var catalog = ServerCatalog.LoadEmbedded();

        await builder.GetAsync(catalog, learned: null);
        Assert.Equal(3, handler.Requested.Count);

        await builder.GetAsync(catalog, learned: null);
        Assert.Equal(3, handler.Requested.Count); // fresh cache, no new downloads

        await builder.GetAsync(catalog, learned: null, forceRefresh: true);
        Assert.Equal(6, handler.Requested.Count);
    }

    [Fact]
    public void Old_or_incomplete_snapshots_are_stale()
    {
        var b = new RangeBuilder(new HttpClient(AllOk()), Cache);
        var now = DateTimeOffset.Now;
        Assert.True(b.IsStale(new SourceSnapshot()));
        Assert.True(b.IsStale(new SourceSnapshot { GcpFetched = now, AwsFetched = now }));
        Assert.True(b.IsStale(new SourceSnapshot { GcpFetched = now, AwsFetched = now, AsnFetched = now - TimeSpan.FromDays(8) }));
        Assert.False(b.IsStale(new SourceSnapshot { GcpFetched = now, AwsFetched = now, AsnFetched = now - TimeSpan.FromDays(6) }));
    }

    [Fact]
    public void A_corrupt_cache_file_is_treated_as_empty()
    {
        File.WriteAllText(Cache, "{{{ garbage");
        var snap = new RangeBuilder(new HttpClient(AllOk()), Cache).LoadSnapshot();
        Assert.Null(snap.GcpFetched);
        Assert.Empty(snap.Gcp);
    }

    [Fact]
    public void Build_warns_about_regions_missing_from_downloaded_lists()
    {
        var snap = new SourceSnapshot { GcpFetched = DateTimeOffset.Now, AwsFetched = DateTimeOffset.Now };
        var set = RangeBuilder.Build(ServerCatalog.LoadEmbedded(), snap, learned: null);

        Assert.Contains(set.For("gmec1")!.Warnings, w => w.Contains("me-central1"));
        Assert.Contains(set.For("mes1")!.Warnings, w => w.Contains("me-south-1"));
        Assert.Contains(set.For("gmec1")!.Warnings, w => w.Contains("No IP ranges"));
        Assert.Empty(set.For("gmec1")!.Blocks);
    }

    [Fact]
    public void Build_with_nothing_downloaded_still_resolves_curated_blizzard_ranges()
    {
        var set = RangeBuilder.Build(ServerCatalog.LoadEmbedded(), new SourceSnapshot(), learned: null);
        Assert.NotEmpty(set.For("ams1")!.Blocks);
        Assert.Contains(set.Messages, m => m.Contains("never been downloaded"));
    }
}
