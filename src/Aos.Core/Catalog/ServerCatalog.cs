using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aos.Core.Catalog;

public sealed record ServerDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Location { get; init; } = "";
    public required string Region { get; init; }
    public bool Experimental { get; init; }
    public List<string> Blizzard { get; init; } = [];
    public List<string> Gcp { get; init; } = [];
    public List<string> Aws { get; init; } = [];
    public List<string> Extra { get; init; } = [];
    public string? Ping { get; init; }

    public string DisplayName => $"{Name} ({Code.ToUpperInvariant()})";
}

public sealed class ServerCatalog
{
    public const string FileName = "servers.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public required IReadOnlyList<ServerDefinition> Servers { get; init; }
    public string Origin { get; init; } = "embedded";

    public IEnumerable<string> Regions => Servers.Select(s => s.Region).Distinct();

    public ServerDefinition? Find(string code) =>
        Servers.FirstOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads the user's override from the data folder if present, otherwise the embedded catalog.</summary>
    public static ServerCatalog Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? AppPaths.DataDirectory, FileName);
        if (File.Exists(path))
        {
            using var fs = File.OpenRead(path);
            return Parse(fs, path);
        }
        return LoadEmbedded();
    }

    public static ServerCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aos.Core.servers.json")
            ?? throw new InvalidOperationException("Embedded servers.json is missing.");
        return Parse(stream, "embedded");
    }

    public static ServerCatalog Parse(Stream json, string origin)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions)
            ?? throw new InvalidDataException("servers.json is empty.");
        var dupes = file.Servers.GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) throw new InvalidDataException($"Duplicate server codes: {string.Join(", ", dupes)}");
        return new ServerCatalog { Servers = file.Servers, Origin = origin };
    }

    private sealed record CatalogFile
    {
        public int Version { get; init; }
        public List<ServerDefinition> Servers { get; init; } = [];
    }
}
