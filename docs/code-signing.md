# Code signing (SignPath Foundation)

Windows SmartScreen warns about any downloaded program that isn't signed by a certificate it already trusts
("Windows protected your PC" / "Unknown publisher"). The self-signed certificate `build-installer.ps1` used
before doesn't help: nobody's PC trusts it. The fix is a real code-signing certificate, and for open-source
projects the [SignPath Foundation](https://signpath.org) provides one for free. Its certificate already has
SmartScreen reputation, so signed releases stop showing the warning.

The release workflow is ready for it: once the secret below exists, every release signs `ReeledSetup.exe`,
`Reeled.exe` and `Uninstall.exe`, puts the signed app back into `payload.zip`, and only then computes the
checksums the in-app updater verifies. Until then releases build unsigned, exactly as before.

## 1. Apply

1. Reeled qualifies: public repository, GPL-3.0 licence, released binaries built by GitHub Actions.
2. Add a code signing policy to the README (draft below), since SignPath checks for it.
3. Apply at <https://signpath.org/apply> with the repository URL.

## 2. Set up the SignPath project (after approval)

In the SignPath web app, in the organisation SignPath gives you:

- **Project** with slug `Reeled` (or set a repository variable `SIGNPATH_PROJECT_SLUG`).
- **Trusted build system**: link *GitHub.com* to the project, so only this repository's workflow can submit.
- **Artifact configuration** (set as the project's default):

  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
    <zip-file>
      <pe-file path="ReeledSetup.exe">
        <authenticode-sign />
      </pe-file>
      <pe-file path="Reeled.exe">
        <authenticode-sign />
      </pe-file>
      <pe-file path="Uninstall.exe">
        <authenticode-sign />
      </pe-file>
    </zip-file>
  </artifact-configuration>
  ```

- **Signing policy** with slug `release-signing` (the Foundation's usual name; otherwise set
  `SIGNPATH_POLICY_SLUG`). Releases signed with it need an approval in SignPath before they finish.

## 3. Connect the repository

Repository → Settings → Secrets and variables → Actions:

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath user with *Submitter* rights on the project |
| Variable | `SIGNPATH_ORGANIZATION_ID` | The organisation ID from SignPath |
| Variable (optional) | `SIGNPATH_PROJECT_SLUG` | Only if the project slug isn't `Reeled` |
| Variable (optional) | `SIGNPATH_POLICY_SLUG` | Only if the policy slug isn't `release-signing` |

Push a `v*` tag as usual. The *Sign with SignPath* step waits until the request is approved and signed
(up to 10 minutes), and the last signing step prints each file's signature status in the log.

## Draft code signing policy (for the README)

> ### Code signing policy
>
> Free code signing provided by [SignPath.io](https://signpath.io), certificate by
> [SignPath Foundation](https://signpath.org).
>
> - Committers and reviewers: [k25jura](https://github.com/k25jura)
> - Approvers: [k25jura](https://github.com/k25jura)
>
> Releases are built from this repository by GitHub Actions; only those builds are signed.
>
> **Privacy:** Reeled doesn't send any information to other networked systems unless you ask it to, or
> except for these features, which you can turn off: checking GitHub for new releases (Settings →
> Updates), and looking up game icons for folder names on Steam, plus SteamGridDB and IGDB when you add
> their keys (Settings → Appearance).
