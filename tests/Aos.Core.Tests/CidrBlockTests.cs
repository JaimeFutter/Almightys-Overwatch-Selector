using System.Net;
using Aos.Core.Net;

namespace Aos.Core.Tests;

public class CidrBlockTests
{
    [Theory]
    [InlineData("64.224.26.0/23", "64.224.26.0/23")]
    [InlineData("64.224.27.9/23", "64.224.26.0/23")] // host bits are cleared
    [InlineData("8.8.8.8", "8.8.8.8/32")]
    [InlineData("2600:1900:4000::/44", "2600:1900:4000::/44")]
    public void Parse_normalizes(string input, string expected) =>
        Assert.Equal(expected, CidrBlock.Parse(input).ToString());

    [Fact]
    public void ParseMany_expands_start_end_ranges()
    {
        var blocks = CidrBlock.ParseMany("5.42.168.0-5.42.175.255");
        Assert.Equal(["5.42.168.0/21"], blocks.Select(b => b.ToString()));

        var uneven = CidrBlock.ParseMany("10.0.0.1-10.0.0.6");
        Assert.Equal(["10.0.0.1/32", "10.0.0.2/31", "10.0.0.4/31", "10.0.0.6/32"], uneven.Select(b => b.ToString()));
    }

    [Fact]
    public void ParseMany_skips_blank_and_comment_lines()
    {
        Assert.Empty(CidrBlock.ParseMany("   "));
        Assert.Empty(CidrBlock.ParseMany("# comment"));
    }

    [Fact]
    public void Contains_respects_family_and_bounds()
    {
        var b = CidrBlock.Parse("34.166.0.0/16");
        Assert.True(b.Contains(IPAddress.Parse("34.166.0.84")));
        Assert.True(b.Contains(IPAddress.Parse("34.166.255.255")));
        Assert.False(b.Contains(IPAddress.Parse("34.167.0.0")));
        Assert.False(b.Contains(IPAddress.Parse("::ffff:34.167.0.0")));
        Assert.True(b.Contains(IPAddress.Parse("::ffff:34.166.1.1")));
        Assert.False(b.Contains(IPAddress.Parse("2600::1")));
    }

    [Fact]
    public void Merge_joins_adjacent_and_drops_duplicates_and_subnets()
    {
        var merged = CidrBlock.Merge(new[]
        {
            "64.224.0.0/22", "64.224.4.0/22",   // adjacent -> /21
            "64.224.1.0/24",                    // inside
            "64.224.0.0/22",                    // duplicate
            "24.105.40.0/21",
            "2a04:e802::/48", "2a04:e802:1::/48",
        }.Select(CidrBlock.Parse));

        Assert.Equal(["24.105.40.0/21", "64.224.0.0/21", "2a04:e802::/47"], merged.Select(b => b.ToString()));
    }

    [Fact]
    public void Merge_of_unaligned_run_produces_minimal_cover()
    {
        var merged = CidrBlock.Merge(new[] { "10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24" }.Select(CidrBlock.Parse));
        Assert.Equal(["10.0.1.0/24", "10.0.2.0/23"], merged.Select(b => b.ToString()));
    }

    [Fact]
    public void Widen_turns_a_host_into_its_subnet()
    {
        Assert.Equal("34.166.0.0/24", CidrBlock.Parse("34.166.0.84").Widen(24).ToString());
        Assert.Equal("2600:1900:4000:1::/64", CidrBlock.Parse("2600:1900:4000:1::5").Widen(64).ToString());
    }

    [Fact]
    public void Full_ipv4_space_round_trips()
    {
        var all = CidrBlock.ParseMany("0.0.0.0-255.255.255.255");
        Assert.Equal(["0.0.0.0/0"], all.Select(b => b.ToString()));
    }
}
