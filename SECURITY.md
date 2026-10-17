# Security Policy

## Supported versions

Only the latest release receives fixes. Reeled updates itself from Settings › Updates, or you can download the newest installer from the [releases page](https://github.com/k25jura/Reeled/releases/latest).

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Report them privately through
[GitHub security advisories](https://github.com/k25jura/Reeled/security/advisories/new) with:

- the Reeled version (Settings › About),
- what an attacker could do and how,
- steps or a file that reproduces it.

You'll get a reply as soon as possible, and credit in the release notes if you'd like.

## How updates are verified

The in-app updater downloads `payload.zip` from the official GitHub release over HTTPS and checks it against the SHA-256 published in that release's `checksums.txt` before installing.
