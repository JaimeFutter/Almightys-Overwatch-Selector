using System.Net;
using System.Net.Sockets;
using Aos.Core.Game;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Aos.Core.Learn;

public sealed record ObservedServer(IPAddress Address, int PacketsPerSecond, long Bytes);

/// <summary>
/// Watches the UDP traffic Overwatch.exe sends (kernel ETW network events, needs admin).
/// During a match the game streams ~60+ packets a second to one address: that's the game server.
/// </summary>
public sealed class ServerObserver : IDisposable
{
    public const string SessionName = "AOS-Learn";
    /// <summary>Packets per second a destination must sustain to count as the match server.</summary>
    public const int MatchThreshold = 20;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly TrafficAnalyzer _traffic = new(Window, MatchThreshold);
    private TraceEventSession? _session;
    private Thread? _thread;
    private Timer? _timer;
    private HashSet<int> _pids = [];

    /// <summary>Raised when the match server changes (null when the match ends).</summary>
    public event Action<ObservedServer?>? ServerChanged;
    /// <summary>Raised once a second with the busiest destinations.</summary>
    public event Action<IReadOnlyList<ObservedServer>>? Tick;
    public event Action<string>? Error;

    public bool IsRunning => _session is not null;

    public void Start()
    {
        if (IsRunning) return;
        if (!TraceEventSession.IsElevated() ?? false)
            throw new UnauthorizedAccessException("Learn mode needs administrator rights.");

        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var kernel = _session.Source.Kernel;
        kernel.UdpIpSend += e => OnSend(e.ProcessID, e.daddr, e.size);
        kernel.UdpIpSendIPV6 += e => OnSend(e.ProcessID, e.daddr, e.size);

        _thread = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch (Exception ex) { Error?.Invoke(ex.Message); }
        }) { IsBackground = true, Name = "AOS ETW" };
        _thread.Start();

        RefreshPids();
        _timer = new Timer(_ => Evaluate(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer?.Dispose(); _timer = null;
        _session?.Dispose(); _session = null;
        _thread = null;
    }

    public void Dispose() => Stop();

    private int _tickCount;

    private void RefreshPids() => _pids = GameLocator.RunningProcessIds().ToHashSet();

    private void OnSend(int pid, IPAddress? daddr, int size)
    {
        if (daddr is null || !_pids.Contains(pid) || !IsPublic(daddr)) return;
        _traffic.Add(DateTime.UtcNow, daddr, size);
    }

    private void Evaluate()
    {
        if (++_tickCount % 5 == 0) RefreshPids();

        var top = _traffic.Top(DateTime.UtcNow);
        Tick?.Invoke(top);
        if (_traffic.Evaluate(top, out var server)) ServerChanged?.Invoke(server);
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return !(b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224 || b[0] == 0);
        }
        return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal);
    }
}
