using System.Net;
using System.Text.Json;
using Aos.Core.Catalog;
using Aos.Core.Net;

namespace Aos.Core.Learn;

/// <summary>A game server IP observed during a match.</summary>
public sealed record LearnedServer
{
    public required string Ip { get; init; }
    /// <summary>Server code this IP was assigned to (automatically or by the user); null while unknown.</summary>
    public string? ServerCode { get; set; }
    public bool AssignedByUser { get; set; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; set; }
    public int Sightings { get; set; }

    /// <summary>The block that gets added to the server's list: /24 for IPv4, /64 for IPv6.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public CidrBlock Block
    {
        get
        {
            var single = CidrBlock.Parse(Ip);
            return single.Widen(single.IsV6 ? 64 : 24);
        }
    }
}

/// <summary>Persists learned server IPs to learned.json in the data folder.</summary>
public sealed class LearnedStore
{
    public const string FileName = "learned.json";
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<LearnedServer> _items;

    public LearnedStore(string? path = null)
    {
        _path = path ?? AppPaths.File(FileName);
        _items = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<LearnedServer>>(File.ReadAllText(_path), ServerCatalog.JsonOptions) ?? []
            : [];
    }

    public IReadOnlyList<LearnedServer> Items { get { lock (_gate) return _items.ToList(); } }

    public IEnumerable<CidrBlock> BlocksFor(string serverCode)
    {
        lock (_gate)
            return _items.Where(i => string.Equals(i.ServerCode, serverCode, StringComparison.OrdinalIgnoreCase))
                         .Select(i => i.Block).ToList();
    }

    /// <summary>Records a sighting. A user's earlier assignment is never overwritten by automatic classification.</summary>
    public LearnedServer Record(IPAddress ip, string? classifiedAs)
    {
        lock (_gate)
        {
            string key = ip.ToString();
            var now = DateTimeOffset.Now;
            var item = _items.FirstOrDefault(i => i.Ip == key);
            if (item is null)
            {
                item = new LearnedServer { Ip = key, ServerCode = classifiedAs, FirstSeen = now, LastSeen = now, Sightings = 1 };
                _items.Add(item);
            }
            else
            {
                item.LastSeen = now;
                item.Sightings++;
                if (!item.AssignedByUser && classifiedAs is not null) item.ServerCode = classifiedAs;
            }
            Save();
            return item;
        }
    }

    public void Assign(string ip, string? serverCode)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Ip == ip) ?? throw new KeyNotFoundException(ip);
            item.ServerCode = serverCode;
            item.AssignedByUser = serverCode is not null;
            Save();
        }
    }

    public void Remove(string ip)
    {
        lock (_gate) { _items.RemoveAll(i => i.Ip == ip); Save(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_items, ServerCatalog.JsonOptions));
    }
}
