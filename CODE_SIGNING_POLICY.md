# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## What gets signed

Only release builds produced by this repository's GitHub Actions workflow
([`.github/workflows/release.yml`](.github/workflows/release.yml)) from a tagged commit (`v*`) on
the `main` branch are submitted for signing. That covers:

- `AlmightysOverwatchSelector.exe` and this project's own `Aos.*.dll` assemblies
- the installer `AlmightysOverwatchSelector-Setup-<version>.exe`

Third-party files (the .NET and Windows App SDK runtimes, Community Toolkit, TraceEvent) keep
their original publishers' signatures and are never re-signed.

Builds made anywhere else, including local builds and forks, are not signed by this project.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [JaimeFutter](https://github.com/JaimeFutter) |
| Approvers | [JaimeFutter](https://github.com/JaimeFutter) |

Every signing request is approved manually by an approver in SignPath. Multi-factor authentication
is required on GitHub and SignPath for all team members.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it, with these exceptions, which are
needed for it to work:

- **IP range downloads.** To know which addresses belong to which game region, the app downloads
  public, anonymous lists from Google (`www.gstatic.com/ipranges/cloud.json`), Amazon
  (`ip-ranges.amazonaws.com/ip-ranges.json`) and RIPE NCC (`stat.ripe.net`). This happens when you
  click **Refresh IPs**, and automatically when the cached copy is older than 7 days. No personal
  data, identifiers, or usage information are sent, only standard HTTP requests.
- **Update check.** Once a day, the app asks GitHub's public releases API
  (`api.github.com/repos/JaimeFutter/Almightys-Overwatch-Selector/releases`) whether a newer version exists.
  It sends nothing but a standard HTTPS request. You can turn this off in **Settings → Updates**, or check
  manually with **Check now**. Downloads are never automatic: the app only opens the release page.
- **Ping.** When you click **Ping** (and once when the Servers page opens), the app sends ICMP echo
  requests, or makes a TCP connection if ping is blocked, to one public address per game server to
  show latency.

The app has no telemetry, analytics or accounts. Learn mode observes Overwatch's
network traffic **locally** to identify the match server. Nothing it observes leaves your PC; learned
server IPs are stored only in `%LOCALAPPDATA%\AlmightysOverwatchSelector`.
