# Contributing to Reeled

Thanks for wanting to help! Bug reports, ideas and pull requests are all welcome.

## Reporting bugs and ideas

The easiest way is **Send feedback** in the app (sidebar, or Settings › About). It opens the right GitHub form
with your version and diagnostics already filled in. You can also use the [issue forms](https://github.com/k25jura/Reeled/issues/new/choose) directly.

## Building

Requirements: Windows 10 1809+ (x64), Visual Studio 2022 or the .NET 8 SDK, and the Windows App SDK workload.

```powershell
dotnet restore Reeled.sln -r win-x64
dotnet build Reeled/Reeled.csproj -c Debug -r win-x64
```

Run `Reeled/bin/Debug/net8.0-windows10.0.26100.0/win-x64/Reeled.exe`. Passing a video path opens it straight in the player.

The full installer is built with `scripts/build-installer.ps1 -Version x.y.z`.

## Pull requests

- Branch from `main` and keep each PR focused on one change.
- Keep the WinUI 3 / Windows 11 look: Didact Gothic typography, the shared motion tokens in `Helpers/Motion.cs`
  (start storyboards with `Motion.Begin`), and no decorative pills or backgrounds that don't carry information.
- Every visible string goes through `Localization/en.json` / could be without `Localization/uk.json` (eventually other languages will be added).
- Check both Dark and Light themes, and Reduce motion on and off.
- `dotnet build` should finish with no new warnings.

## Project layout

| Folder | What's inside |
| --- | --- |
| `Reeled/Views` | Pages (library, player, settings) |
| `Reeled/ViewModels` | MVVM view models (CommunityToolkit.Mvvm) |
| `Reeled/Services` | Indexing, metadata cache, playback (LibVLC), updates, storage |
| `Reeled/Helpers` | Motion system, video glass, release notes renderer, logging |
| `Reeled.Installer` | WinUI 3 installer and uninstaller |
