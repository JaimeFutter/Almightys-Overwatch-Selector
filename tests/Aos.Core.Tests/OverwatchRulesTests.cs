using System.Xml.Linq;
using Aos.Core.Firewall;

namespace Aos.Core.Tests;

public class OverwatchRulesTests
{
    private const string Exe = @"C:\Program Files (x86)\Overwatch\_retail_\Overwatch.exe";

    [Theory]
    [InlineData("AOS - MES1 - 1 out", FirewallService.Group, "", true, RuleSource.Ours)]
    [InlineData("some rule", "stormy/dropship/v3", "", true, RuleSource.Dropship)]
    [InlineData("old", "stormy.gg/dropship", "", true, RuleSource.Dropship)]
    [InlineData("Overwatch Application", "", "Overwatch Application", false, RuleSource.WindowsPrompt)]
    [InlineData("Block EU", "", "", true, RuleSource.Other)]
    [InlineData("OW block", "Overwatch Server Selector", "", true, RuleSource.Other)]
    public void Classifies_rule_sources(string name, string group, string description, bool block, RuleSource expected) =>
        Assert.Equal(expected, OverwatchRules.Classify(name, group, description, block));

    [Theory]
    [InlineData("Overwatch Application", "", @"C:\program files (x86)\overwatch\_retail_\overwatch.exe", true)]
    [InlineData("Block servers", "", Exe, true)]                       // matched by program path
    [InlineData("anything", "stormy/dropship/v3", "", true)]           // matched by dropship group
    [InlineData("Overwatch EU", "", "", true)]                         // matched by name
    [InlineData("Chrome", "", @"C:\Program Files\Google\Chrome\chrome.exe", false)]
    [InlineData("Overwatch2Helper", "", @"C:\tools\notoverwatch.exe", true)] // name mentions Overwatch
    [InlineData("Steam", "", @"C:\Steam\steam.exe", false)]
    public void Detects_rules_related_to_overwatch(string name, string group, string program, bool expected) =>
        Assert.Equal(expected, OverwatchRules.IsRelated(name, group, program));

    [Fact]
    public void Fingerprint_is_stable_and_distinguishes_rules()
    {
        string a = OverwatchRules.Fingerprint("Overwatch Application", "", Exe, false, true, 6, "*", "");
        string b = OverwatchRules.Fingerprint("Overwatch Application", "", Exe, false, true, 17, "*", "");
        Assert.Equal(a, OverwatchRules.Fingerprint("Overwatch Application", "", Exe, false, true, 6, "*", ""));
        Assert.NotEqual(a, b); // TCP vs UDP twin rules get different keys
    }

    [Fact]
    public void Parses_dropship_filters_from_netsh_output()
    {
        var xml = XDocument.Parse($"""
            <wfpdiag><filters>
              <item>
                <filterKey>{"{"}11111111-1111-1111-1111-111111111111{"}"}</filterKey>
                <displayData><name>ams1</name></displayData>
                <layerKey>FWPM_LAYER_ALE_AUTH_CONNECT_V4</layerKey>
                <subLayerKey>{"{"}{DropshipFilters.SubLayerKey}{"}"}</subLayerKey>
                <providerKey>{"{"}{DropshipFilters.ProviderKey}{"}"}</providerKey>
                <filterId>70123</filterId>
              </item>
              <item>
                <filterKey>{"{"}22222222-2222-2222-2222-222222222222{"}"}</filterKey>
                <displayData><name>Some Windows filter</name></displayData>
                <layerKey>FWPM_LAYER_ALE_AUTH_CONNECT_V4</layerKey>
                <filterId>5</filterId>
              </item>
            </filters></wfpdiag>
            """);

        var (filters, provider) = DropshipFilters.Parse(xml);
        var f = Assert.Single(filters);
        Assert.Equal("ams1", f.Name);
        Assert.Equal(70123UL, f.Id);
        Assert.True(provider);
    }
}
