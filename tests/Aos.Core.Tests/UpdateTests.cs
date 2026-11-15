using System.Text;
using Aos.Core.Updates;

namespace Aos.Core.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("v0.9.1", 0, 9, 1, "")]
    [InlineData("1.0.0", 1, 0, 0, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1.0.0-beta.2", 1, 0, 0, "beta.2")]
    [InlineData("1.0.0+6029c07", 1, 0, 0, "")]          // build metadata (InformationalVersion) ignored
    [InlineData("0.9.1-rc.1+abc", 0, 9, 1, "rc.1")]
    public void Parses(string text, int major, int minor, int patch, string pre) =>
        Assert.Equal(new SemVersion(major, minor, patch, pre), SemVersion.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.0.0-")]
    [InlineData("1.x.0")]
    [InlineData("1.2.3.4")]
    public void Rejects_non_versions(string text) => Assert.False(SemVersion.TryParse(text, out _));

    [Fact]
    public void Orders_by_semver_rules()
    {
        string[] ascending =
        [
            "0.9.0", "0.9.1", "0.10.0", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta",
            "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "2.0.0",
        ];
        var parsed = ascending.Select(SemVersion.Parse).ToList();
        Assert.Equal(parsed, parsed.OrderBy(v => v).ToList());
        for (int i = 1; i < parsed.Count; i++) Assert.True(parsed[i] > parsed[i - 1], $"{parsed[i]} > {parsed[i - 1]}");
        Assert.Equal(0, SemVersion.Parse("v1.0.0").CompareTo(SemVersion.Parse("1.0.0+meta")));
    }

    [Theory]
    [InlineData("0.9.1", true)]
    [InlineData("1.0.0-beta.1", true)]
    [InlineData("1.0.0", false)]
    [InlineData("2.3.4", false)]
    public void Pre_release_channel(string v, bool expected) => Assert.Equal(expected, SemVersion.Parse(v).OnPreReleaseChannel);
}

public class UpdateCheckerTests
{
    private static ReleaseInfo R(string tag, bool pre = false, bool draft = false) =>
        new(SemVersion.Parse(tag), tag, $"https://example/{tag}", pre, draft, null);

    private static readonly List<ReleaseInfo> Releases =
    [
        R("v0.9.0", pre: true), R("v0.9.1", pre: true), R("v0.9.2", pre: true),
        R("v1.0.0"), R("v1.1.0-beta.1", pre: true), R("v1.0.1"), R("v1.2.0", draft: true),
    ];

    [Fact]
    public void Pre_release_users_get_the_newest_release_of_any_kind()
    {
        Assert.Equal("v1.1.0-beta.1", UpdateChecker.PickUpdate(Releases, SemVersion.Parse("0.9.1"))!.Tag);
    }

    [Fact]
    public void Stable_users_only_get_stable_releases()
    {
        Assert.Equal("v1.0.1", UpdateChecker.PickUpdate(Releases, SemVersion.Parse("1.0.0"))!.Tag);
    }

    [Fact]
    public void Drafts_are_never_offered_and_up_to_date_returns_null()
    {
        Assert.Null(UpdateChecker.PickUpdate(Releases, SemVersion.Parse("1.1.0")));      // only the 1.2.0 draft is newer
        Assert.Null(UpdateChecker.PickUpdate([], SemVersion.Parse("0.9.1")));
    }

    [Fact]
    public void Older_or_equal_versions_are_not_updates()
    {
        Assert.Null(UpdateChecker.PickUpdate([R("v0.9.0", pre: true), R("v0.9.1", pre: true)], SemVersion.Parse("0.9.1")));
    }

    [Fact]
    public async Task Parses_the_github_releases_response()
    {
        const string json = """
            [
              { "tag_name": "v0.9.1", "html_url": "https://github.com/x/y/releases/tag/v0.9.1", "prerelease": true, "draft": false, "published_at": "2026-11-15T18:49:28Z" },
              { "tag_name": "nightly", "html_url": "https://github.com/x/y/releases/tag/nightly", "prerelease": true, "draft": false, "published_at": null },
              { "tag_name": "v0.9.0", "html_url": "https://github.com/x/y/releases/tag/v0.9.0", "prerelease": true, "draft": false, "published_at": "2026-11-15T13:57:43Z" }
            ]
            """;
        var list = await UpdateChecker.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(["v0.9.1", "v0.9.0"], list.Select(r => r.Tag));   // "nightly" isn't a version, skipped
        Assert.True(list[0].IsPreRelease);
        Assert.Equal(2026, list[0].PublishedAt!.Value.Year);
    }

    [Fact]
    public async Task Check_requests_the_api_and_picks_the_update()
    {
        var handler = new FakeHandler();
        handler.Routes[UpdateChecker.ApiUrl] = () => FakeHandler.Json("""
            [ { "tag_name": "v0.9.2", "html_url": "https://github.com/x/y/releases/tag/v0.9.2", "prerelease": true, "draft": false } ]
            """);
        var update = await UpdateChecker.CheckAsync(new HttpClient(handler), SemVersion.Parse("0.9.1"));
        Assert.Equal("v0.9.2", update!.Tag);
        Assert.Single(handler.Requested);
    }
}
