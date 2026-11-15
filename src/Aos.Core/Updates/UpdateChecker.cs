using System.Net.Http.Headers;
using System.Text.Json;

namespace Aos.Core.Updates;

public sealed record ReleaseInfo(SemVersion Version, string Tag, string Url, bool IsPreRelease, bool IsDraft, DateTimeOffset? PublishedAt);

/// <summary>
/// Asks GitHub's public releases API whether a newer version exists. It only reads the public release
/// list; nothing about the user or their PC is sent beyond a normal HTTPS request.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "JaimeFutter/Almightys-Overwatch-Selector";
    public const string ApiUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=30";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    /// <summary>Returns the newest release the user should be offered, or null when they're up to date.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(HttpClient http, SemVersion current, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (request.Headers.UserAgent.Count == 0 && http.DefaultRequestHeaders.UserAgent.Count == 0)
            request.Headers.UserAgent.ParseAdd($"AlmightysOverwatchSelector/{current}");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return PickUpdate(await ParseAsync(stream, ct), current);
    }

    public static async Task<List<ReleaseInfo>> ParseAsync(Stream json, CancellationToken ct = default)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var list = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!SemVersion.TryParse(tag, out var version)) continue;   // ignore tags that aren't versions
            list.Add(new ReleaseInfo(
                version,
                tag,
                r.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage,
                r.TryGetProperty("prerelease", out var p) && p.GetBoolean(),
                r.TryGetProperty("draft", out var d) && d.GetBoolean(),
                r.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(pa.GetString(), out var when) ? when : null));
        }
        return list;
    }

    /// <summary>
    /// The newest non-draft release above <paramref name="current"/>. Users on a pre-release channel
    /// (0.x or a suffixed version) are also offered pre-releases; users on a stable 1.x+ build only
    /// get stable releases.
    /// </summary>
    public static ReleaseInfo? PickUpdate(IEnumerable<ReleaseInfo> releases, SemVersion current) =>
        releases
            .Where(r => !r.IsDraft)
            .Where(r => current.OnPreReleaseChannel || !(r.IsPreRelease || r.Version.IsPreRelease))
            .Where(r => r.Version > current)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
}
