using System.Text.Json;
using Aos.Core.Net;

namespace Aos.Core.Sources;

/// <summary>AWS's published IP ranges. Only EC2 ranges are kept (that's where game servers run), grouped by region.</summary>
public static class AwsRangeSource
{
    public const string Url = "https://ip-ranges.amazonaws.com/ip-ranges.json";

    public static async Task<Dictionary<string, List<CidrBlock>>> FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        await using var stream = await http.GetStreamAsync(Url, ct);
        return await ParseAsync(stream, ct);
    }

    public static async Task<Dictionary<string, List<CidrBlock>>> ParseAsync(Stream json, CancellationToken ct = default)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var result = new Dictionary<string, List<CidrBlock>>(StringComparer.OrdinalIgnoreCase);
        Read(doc.RootElement, "prefixes", "ip_prefix", result);
        Read(doc.RootElement, "ipv6_prefixes", "ipv6_prefix", result);
        return result;
    }

    private static void Read(JsonElement root, string arrayName, string field, Dictionary<string, List<CidrBlock>> result)
    {
        if (!root.TryGetProperty(arrayName, out var arr)) return;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.GetProperty("service").GetString() != "EC2") continue;
            string? region = p.GetProperty("region").GetString();
            string? prefix = p.GetProperty(field).GetString();
            if (region is null || prefix is null || !CidrBlock.TryParse(prefix, out var block)) continue;
            if (!result.TryGetValue(region, out var list)) result[region] = list = [];
            list.Add(block);
        }
    }
}
