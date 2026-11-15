# Security policy

Almighty's Overwatch Selector runs as administrator and changes Windows Firewall rules, so security
reports are taken seriously.

## Supported versions

Only the latest release gets fixes.

## Reporting a vulnerability

**Please don't open a public issue.** Report it privately through
[GitHub's private vulnerability reporting](https://github.com/JaimeFutter/Almightys-Overwatch-Selector/security/advisories/new).

Please include what an attacker could do, the steps to reproduce it, and the app version. You'll get
a reply within a week. Once a fix is released, you'll be credited in the advisory unless you'd rather
not be.

Examples of what counts:
- the app creating, changing or deleting firewall rules or WFP filters it shouldn't touch;
- running code or loading files from places a non-admin user can write to;
- release files that don't match `SHA256SUMS.txt`, or signing problems.

## Verifying downloads

Official builds are only published on the
[Releases page](https://github.com/JaimeFutter/Almightys-Overwatch-Selector/releases). Check files against
the release's `SHA256SUMS.txt`:

```powershell
Get-FileHash .\AlmightysOverwatchSelector-Setup-1.0.0.exe -Algorithm SHA256
```

Signed builds show the publisher **SignPath Foundation** (see [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md)).
