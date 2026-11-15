# Third-party notices

Almighty's Overwatch Selector bundles the following components. Each stays under its own license;
the GPL-3.0 that covers this project does not apply to them (see the section 7 permission in
[NOTICE.md](NOTICE.md)).

| Component | License | Notes |
|---|---|---|
| .NET runtime (Microsoft) | MIT | Bundled in self-contained builds. <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT> |
| Windows App SDK / WinUI 3 runtime (Microsoft) | Microsoft Software License Terms | Redistributable files bundled in self-contained builds. Full terms: [licenses/WindowsAppSDK-LICENSE.txt](licenses/WindowsAppSDK-LICENSE.txt). By using this app you also agree to those terms for the bundled Microsoft components. |
| CommunityToolkit.WinUI.Controls.SettingsControls (.NET Foundation and contributors) | MIT | [licenses/CommunityToolkit-LICENSE.md](licenses/CommunityToolkit-LICENSE.md) |
| Microsoft.Diagnostics.Tracing.TraceEvent (Microsoft) | MIT | <https://github.com/microsoft/perfview/blob/main/LICENSE.TXT> |

## Data sources (not bundled; downloaded at runtime)

- Google Cloud IP ranges: <https://www.gstatic.com/ipranges/cloud.json>
- AWS IP ranges: <https://ip-ranges.amazonaws.com/ip-ranges.json>
- RIPEstat announced prefixes for AS57976: <https://stat.ripe.net/>

## Acknowledgements

The server catalog was cross-checked against the community projects
[foryVERX/Overwatch-Server-Selector](https://github.com/foryVERX/Overwatch-Server-Selector) and
[stowmyy/dropship](https://github.com/stowmyy/dropship). No code from either project is included.
