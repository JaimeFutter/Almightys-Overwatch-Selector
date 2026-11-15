# Almighty's Overwatch Selector

A Windows 11 (WinUI 3) app that picks which Overwatch 2 servers you can be matched on. It blocks `Overwatch.exe` from the regions you don't want, using Windows Firewall rules.
It's built with a South Africa → avoid the Middle East setup in mind, but works for any region.

> Unofficial fan-made tool. Not affiliated with or endorsed by Blizzard Entertainment.
> Overwatch is a trademark of Blizzard Entertainment, Inc.

## Download

Get the latest build from [Releases](https://github.com/ST10067405/Almightys-Overwatch-Selector/releases):

- **`AlmightysOverwatchSelector-Setup-<version>.exe`**: installer (Start Menu shortcut, and uninstalling removes the app's firewall rules)
- **`…-win-x64-portable.zip`**: no install, unzip and run `AlmightysOverwatchSelector.exe`

Needs Windows 10 1809+ or Windows 11 (x64). Nothing else to install, because .NET and the Windows App SDK are bundled.
Check downloads against `SHA256SUMS.txt`. Builds are code signed through SignPath once the project is approved;
until then, SmartScreen may show "Windows protected your PC" (choose **More info → Run anyway**).

## Where the server IPs come from

Blizzard doesn't publish its game server IPs. This app gathers them from four sources:

| Source | What it covers | How |
|---|---|---|
| **Google Cloud** published ranges | The "G" servers: GEN1, GMEC1/2, GSG1, GTK1, GBR1, GUE4… | `gstatic.com/ipranges/cloud.json`, by region |
| **AWS** published ranges | Bahrain/UAE (MES1), Tel Aviv | `ip-ranges.amazonaws.com`, EC2 by region |
| **Curated Blizzard ranges** | AMS1, ORD1, LAS1, SYD2, TPE1, ICN1 | `servers.json`, checked against Blizzard's network **AS57976** through RIPEstat (warns when a range is no longer announced) |
| **Learn mode** | Anything the above misses | Watches the game's UDP traffic during a match (kernel ETW), finds the server, and adds its /24 to that region |

The Middle East is covered from every provider: Google Cloud `me-central1` (Qatar), `me-central2` (Saudi Arabia) and `me-west1` (Tel Aviv), plus AWS `me-south-1` (Bahrain), `me-central-1` (UAE) and `me-west-1`.
All 44 test IPs from the community Middle East list match one of these servers.

Downloads are cached in `%LOCALAPPDATA%\AlmightysOverwatchSelector` and refresh automatically after 7 days. Copy `src/Aos.Core/Catalog/servers.json` into that folder to customise the servers.

## Using it

1. Install or unzip it, then run `AlmightysOverwatchSelector.exe` (it asks for administrator rights; firewall rules need them).
2. On **Servers**, click **Block Middle East** (or use **Only allow…** → Europe), then **Apply**.
3. Restart Overwatch if it was running.
4. Optional: on **Learn**, click **Start watching** and play a match. Check the detected server against the in-game network graph (Ctrl+Shift+N).
5. **Firewall** lists every firewall rule that touches Overwatch, from any tool (this app, dropship, other selectors, rules you made yourself). You can turn rules on or off, delete them, or use **Remove all Overwatch block rules** (Windows' own allow rules are kept). It also finds dropship's hidden WFP filters, which don't show up as firewall rules, and can remove them.
6. Closing the window keeps the app in the system tray (on by default; you can change it in Settings). Click the tray icon to reopen it, or right-click it and choose **Exit**. Launching the app again brings the running copy back instead of opening a second one.

Rules are named `AOS - <SERVER> - n in/out` in the group "Almighty's Overwatch Selector". You can see them in `wf.msc`. **Unblock all** removes them.

## Command line

`src/Aos.Cli` builds `aos.exe`. Use it to check things without the UI:

```
aos dump [server|region] [--refresh] [--full]   # resolved ranges per server
aos classify <ip>...                            # which server/network owns an IP
aos ping
aos apply --allow EU                            # (admin) block every non-EU server
aos block ME                                    # (admin) block just the Middle East
aos status | aos clear | aos learn
aos rules [--remove-blocks]                     # every Overwatch rule, from any tool
aos dropship [--remove]                         # (admin) dropship's leftover WFP filters
```

## Build

Needs the .NET 10 SDK. Everything else comes from NuGet (Windows App SDK).

```
dotnet test tests/Aos.Core.Tests
dotnet build src/Aos.App -p:Platform=x64
```

### Release builds

`scripts/build-release.ps1 -Version 1.0.0` runs the tests and makes a self-contained publish, a portable zip,
the installer ([Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`) and
`SHA256SUMS.txt` in `artifacts/`. Local builds are unsigned.

Official releases come from [`.github/workflows/release.yml`](.github/workflows/release.yml): push a tag such as `v1.0.0`
and it builds everything, signs it through SignPath when configured, and drafts a GitHub release for review.
See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## License

Copyright (C) 2026 Jaime Futter. Free software under the [GNU GPL v3.0 or later](LICENSE), with the additional
terms in [NOTICE.md](NOTICE.md): bundled Microsoft runtime components keep their own license, and the app's
**name and logo are not licensed** for use by modified versions ([TRADEMARKS.md](TRADEMARKS.md)).
Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

Prior art: [foryVERX/Overwatch-Server-Selector](https://github.com/foryVERX/Overwatch-Server-Selector) and [stowmyy/dropship](https://github.com/stowmyy/dropship). Their IP lists were used only to cross-check this one; no code was copied.
