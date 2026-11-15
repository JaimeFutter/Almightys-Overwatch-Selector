using System.Collections.Concurrent;
using System.Net;

namespace Aos.Core.Learn;

/// <summary>
/// Turns a stream of outgoing UDP packets into "which server is the match on". Pure logic (no ETW),
/// so it can be unit tested: packets older than the window are dropped, and the busiest destination
/// counts as the match server once it sustains <see cref="Threshold"/> packets a second.
/// </summary>
public sealed class TrafficAnalyzer(TimeSpan window, int threshold)
{
    private readonly ConcurrentQueue<(DateTime At, IPAddress Ip, int Size)> _packets = new();

    public TimeSpan Window { get; } = window;
    public int Threshold { get; } = threshold;
    public IPAddress? Current { get; private set; }

    public void Add(DateTime at, IPAddress ip, int size) => _packets.Enqueue((at, ip, size));

    /// <summary>Busiest destinations in the window, most packets first.</summary>
    public IReadOnlyList<ObservedServer> Top(DateTime now, int count = 5)
    {
        var cutoff = now - Window;
        while (_packets.TryPeek(out var p) && p.At < cutoff) _packets.TryDequeue(out _);

        return _packets.ToArray()
            .Where(p => p.At <= now)
            .GroupBy(p => p.Ip)
            .Select(g => new ObservedServer(g.Key, (int)(g.Count() / Window.TotalSeconds), g.Sum(x => (long)x.Size)))
            .OrderByDescending(s => s.PacketsPerSecond).ThenByDescending(s => s.Bytes)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Updates <see cref="Current"/>. Returns true when it changed (a new match server, or the match ended),
    /// with <paramref name="server"/> set to the new server or null.
    /// </summary>
    public bool Evaluate(IReadOnlyList<ObservedServer> top, out ObservedServer? server)
    {
        server = top.FirstOrDefault(s => s.PacketsPerSecond >= Threshold);
        if (Equals(server?.Address, Current)) return false;
        Current = server?.Address;
        return true;
    }
}
