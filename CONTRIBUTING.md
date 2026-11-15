# Contributing

Thanks for helping! The most useful contribution is usually a
**[Wrong server / IP report](https://github.com/JaimeFutter/Almightys-Overwatch-Selector/issues/new?template=server_report.yml)**:
Blizzard changes server addresses over time, and reports from players keep the lists accurate.

## Issues

- Pick a form when you open an issue (bug, wrong server/IP, or feature).
- Search existing issues first.
- Never post your own public IP address. Game server IPs are fine.

## Pull requests

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Fork, then branch from `main`.
3. Build and test:
   ```
   dotnet test tests/Aos.Core.Tests
   dotnet build src/Aos.App -p:Platform=x64
   ```
4. Server catalog changes (`src/Aos.Core/Catalog/servers.json`) need evidence: in-game server names
   or IPs from the network graph, or the `aos classify <ip>` output.
5. Keep pull requests focused. CI must pass.

## Code of conduct

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

By contributing, you agree that your contributions are licensed under the project's
[GPL-3.0-or-later license](LICENSE), including the additional terms in [NOTICE.md](NOTICE.md). The name
and logo stay reserved ([TRADEMARKS.md](TRADEMARKS.md)).
