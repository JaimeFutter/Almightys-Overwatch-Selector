using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Aos.Core.Ping;

/// <summary>Measures round-trip time to a host: ICMP first, then a TCP connect to port 443 as a fallback.</summary>
public static class LatencyProbe
{
    public static async Task<int?> MeasureAsync(string host, int attempts = 3, int timeoutMs = 1500, CancellationToken ct = default)
    {
        IPAddress? ip;
        try
        {
            ip = IPAddress.TryParse(host, out var parsed) ? parsed
               : (await Dns.GetHostAddressesAsync(host, ct)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (SocketException) { return null; }
        if (ip is null) return null;

        var samples = new List<long>();
        using (var ping = new System.Net.NetworkInformation.Ping())
        {
            for (int i = 0; i < attempts && !ct.IsCancellationRequested; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(ip, timeoutMs);
                    if (reply.Status == IPStatus.Success) samples.Add(reply.RoundtripTime);
                }
                catch (PingException) { }
            }
        }

        if (samples.Count == 0)
        {
            for (int i = 0; i < attempts && !ct.IsCancellationRequested; i++)
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeoutMs);
                    await client.ConnectAsync(ip, 443, cts.Token);
                    samples.Add(sw.ElapsedMilliseconds);
                }
                catch (Exception e) when (e is SocketException or OperationCanceledException) { }
            }
        }

        if (samples.Count == 0) return null;
        samples.Sort();
        return (int)samples[samples.Count / 2];
    }
}
