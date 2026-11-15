using System.Text.Json;
using Aos.Core.Net;

namespace Aos.Core.Sources;

/// <summary>Google Cloud's published external IP ranges, grouped by region ("scope").</summary>
public static class GcpRangeSource
{
    public const string Url = "https://www.gstatic.com/ipranges/cloud.json";

    public static async Task<Dictionary<string, List<CidrBlock>>> FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        await using var stream = await http.GetStreamAsync(Url, ct);
        return await ParseAsync(stream, ct);
    }

    public static async Task<Dictionary<string, List<CidrBlock>>> ParseAsync(Stream json, CancellationToken ct = default)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var result = new Dictionary<string, List<CidrBlock>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in doc.RootElement.GetProperty("prefixes").EnumerateArray())
        {
            string? scope = p.TryGetProperty("scope", out var s) ? s.GetString() : null;
            string? prefix = p.TryGetProperty("ipv4Prefix", out var v4) ? v4.GetString()
                           : p.TryGetProperty("ipv6Prefix", out var v6) ? v6.GetString() : null;
            if (scope is null || prefix is null || !CidrBlock.TryParse(prefix, out var block)) continue;
            if (!result.TryGetValue(scope, out var list)) result[scope] = list = [];
            list.Add(block);
        }
        return result;
    }
}
