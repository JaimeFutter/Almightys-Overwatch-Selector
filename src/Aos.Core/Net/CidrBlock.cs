using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace Aos.Core.Net;

/// <summary>
/// An IPv4 or IPv6 network in CIDR form. The address is stored as a 128-bit integer
/// (IPv4 uses the low 32 bits) so merging and containment math is the same for both families.
/// </summary>
public readonly record struct CidrBlock(UInt128 Network, int PrefixLength, bool IsV6) : IComparable<CidrBlock>
{
    public int Bits => IsV6 ? 128 : 32;

    public UInt128 First => Network;

    public UInt128 Last => Network | HostMask(PrefixLength, Bits);

    public static CidrBlock Create(IPAddress address, int prefixLength)
    {
        bool v6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        if (address.IsIPv4MappedToIPv6) { address = address.MapToIPv4(); v6 = false; }
        int bits = v6 ? 128 : 32;
        if (prefixLength < 0 || prefixLength > bits)
            throw new FormatException($"Prefix /{prefixLength} out of range for {address}.");
        UInt128 value = ToInteger(address);
        return new CidrBlock(value & ~HostMask(prefixLength, bits), prefixLength, v6);
    }

    /// <summary>Parses "a.b.c.d/nn", a bare address, or a "start-end" range (which may expand to several blocks).</summary>
    public static IReadOnlyList<CidrBlock> ParseMany(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.StartsWith('#')) return [];

        int dash = text.IndexOf('-');
        if (dash > 0)
        {
            var start = IPAddress.Parse(text[..dash].Trim());
            var end = IPAddress.Parse(text[(dash + 1)..].Trim());
            return FromRange(start, end);
        }
        return [Parse(text)];
    }

    public static CidrBlock Parse(string text)
    {
        text = text.Trim();
        int slash = text.IndexOf('/');
        if (slash < 0)
        {
            var ip = IPAddress.Parse(text);
            return Create(ip, ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv4MappedToIPv6 ? 128 : 32);
        }
        return Create(IPAddress.Parse(text[..slash]), int.Parse(text[(slash + 1)..]));
    }

    public static bool TryParse(string text, out CidrBlock block)
    {
        try { block = Parse(text); return true; }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { block = default; return false; }
    }

    /// <summary>Converts an inclusive address range into the minimal list of CIDR blocks.</summary>
    public static IReadOnlyList<CidrBlock> FromRange(IPAddress start, IPAddress end)
    {
        var a = Create(start, start.AddressFamily == AddressFamily.InterNetworkV6 && !start.IsIPv4MappedToIPv6 ? 128 : 32);
        var b = Create(end, end.AddressFamily == AddressFamily.InterNetworkV6 && !end.IsIPv4MappedToIPv6 ? 128 : 32);
        if (a.IsV6 != b.IsV6) throw new FormatException("Range mixes IPv4 and IPv6.");
        if (a.First > b.First) (a, b) = (b, a);
        return Decompose(a.First, b.First, a.IsV6);
    }

    public bool Contains(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        bool v6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        if (v6 != IsV6) return false;
        UInt128 value = ToInteger(address);
        return value >= First && value <= Last;
    }

    public bool Contains(CidrBlock other) =>
        other.IsV6 == IsV6 && other.First >= First && other.Last <= Last;

    public bool Overlaps(CidrBlock other) =>
        other.IsV6 == IsV6 && other.First <= Last && other.Last >= First;

    /// <summary>Widens this block to the given (shorter) prefix, e.g. a learned /32 to its /24.</summary>
    public CidrBlock Widen(int prefixLength) =>
        prefixLength >= PrefixLength ? this : new CidrBlock(Network & ~HostMask(prefixLength, Bits), prefixLength, IsV6);

    public IPAddress NetworkAddress => FromInteger(Network, IsV6);

    public override string ToString() => $"{NetworkAddress}/{PrefixLength}";

    public int CompareTo(CidrBlock other)
    {
        int c = IsV6.CompareTo(other.IsV6);
        if (c != 0) return c;
        c = Network.CompareTo(other.Network);
        return c != 0 ? c : PrefixLength.CompareTo(other.PrefixLength);
    }

    /// <summary>Removes duplicates and contained blocks and joins adjacent blocks into the minimal covering set.</summary>
    public static List<CidrBlock> Merge(IEnumerable<CidrBlock> blocks)
    {
        var result = new List<CidrBlock>();
        foreach (var family in blocks.GroupBy(b => b.IsV6).OrderBy(g => g.Key))
        {
            bool v6 = family.Key;
            var intervals = family.Select(b => (b.First, b.Last)).OrderBy(i => i.First).ToList();
            UInt128 curStart = intervals[0].First, curEnd = intervals[0].Last;
            foreach (var (s, e) in intervals.Skip(1))
            {
                bool adjacent = curEnd != UInt128.MaxValue && s <= curEnd + 1;
                if (adjacent || s <= curEnd)
                {
                    if (e > curEnd) curEnd = e;
                }
                else
                {
                    result.AddRange(Decompose(curStart, curEnd, v6));
                    (curStart, curEnd) = (s, e);
                }
            }
            result.AddRange(Decompose(curStart, curEnd, v6));
        }
        return result;
    }

    private static List<CidrBlock> Decompose(UInt128 start, UInt128 end, bool v6)
    {
        int bits = v6 ? 128 : 32;
        var list = new List<CidrBlock>();
        while (true)
        {
            // Largest block aligned at 'start' that doesn't pass 'end'.
            int maxSize = start == 0 ? bits : (int)UInt128.TrailingZeroCount(start);
            if (maxSize > bits) maxSize = bits;
            UInt128 span = end - start;
            // fit = floor(log2(span + 1)): the largest power-of-two block that fits in the remaining range.
            int fit = span == UInt128.MaxValue ? 128 : 127 - (int)UInt128.LeadingZeroCount(span + 1);
            int size = Math.Min(maxSize, fit);
            list.Add(new CidrBlock(start, bits - size, v6));
            UInt128 blockLast = start + (size >= 128 ? UInt128.MaxValue : (UInt128.One << size) - 1);
            if (blockLast >= end) break;
            start = blockLast + 1;
        }
        return list;
    }

    private static UInt128 HostMask(int prefixLength, int bits)
    {
        int hostBits = bits - prefixLength;
        return hostBits >= 128 ? UInt128.MaxValue : (UInt128.One << hostBits) - 1;
    }

    private static UInt128 ToInteger(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        UInt128 value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }

    private static IPAddress FromInteger(UInt128 value, bool v6)
    {
        int len = v6 ? 16 : 4;
        var bytes = new byte[len];
        for (int i = len - 1; i >= 0; i--) { bytes[i] = (byte)(value & 0xFF); value >>= 8; }
        return new IPAddress(bytes);
    }
}
