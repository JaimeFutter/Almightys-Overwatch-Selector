using System.Text.Json;
using Aos.Core.Net;

namespace Aos.Core.Sources;

/// <summary>
/// Prefixes announced by Blizzard's own network (AS57976), from RIPEstat.
/// Many of these are Battle.net login/chat services rather than game servers, and public geolocation
/// for them is unreliable, so they're used to validate the curated seed ranges and to classify
/// learned server IPs. They're never blocked wholesale.
/// </summary>
public static class BlizzardAsnSource
{
    public const string Asn = "AS57976";
    public const string Url = "https://stat.ripe.net/data/announced-prefixes/data.json?resource=" + Asn;

    public static async Task<List<CidrBlock>> FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        await using var stream = await http.GetStreamAsync(Url, ct);
        return await ParseAsync(stream, ct);
    }

    public static async Task<List<CidrBlock>> ParseAsync(Stream json, CancellationToken ct = default)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var list = new List<CidrBlock>();
        foreach (var p in doc.RootElement.GetProperty("data").GetProperty("prefixes").EnumerateArray())
            if (CidrBlock.TryParse(p.GetProperty("prefix").GetString() ?? "", out var block))
                list.Add(block);
        return list;
    }
}
