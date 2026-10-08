<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="Reeled/Assets/light-banner.svg">
    <source media="(prefers-color-scheme: light)" srcset="Reeled/Assets/dark-banner.svg">
    <img alt="Reeled" src="Reeled/Assets/dark-banner.svg" width="420">
  </picture>
</p>

<p align="center">
  A local video player and library manager for gameplay clips on Windows.
</p>

---

**Reeled** is a desktop player and organizer built for gameplay recordings (NVIDIA Shadowplay, OBS Studio, Medal, SteelSeries Moments, etc.). Instead of digging through folders or opening clips one-by-one in a generic video player, Reeled gives you a dedicated library to browse your recordings and a Direct3D11 player made for reviewing gameplay — with real-time timeline scrubbing, multi-audio track mixing, volume boost, and timestamp bookmarks.

Built with unpackaged WinUI 3 (Windows App SDK), .NET 8, and LibVLC 3.0.

---

## Features

### Library & Folders
- **Folder Watching**: Add your recording directories (e.g. Shadowplay or OBS output folders). Newly recorded, renamed, or deleted clips update automatically.
- **Organization**: Filter clips by date, group by month, and search by title or tags.
- **Clip Context Menu**: View video metadata (resolution, bitrate, codecs, audio tracks), favorite clips, rename, open in File Explorer, or delete to the Windows Recycle Bin.

### Video Player
- **Direct3D11 Playback**: Hardware-accelerated rendering powered by LibVLC 3.0.
- **Timeline Scrubbing**: Smooth video seeking while dragging the timeline slider.
- **Audio Control**: Multi-track audio switching, volume boost above 100%, and scroll-wheel volume adjustment.
- **Playback Tools**: Frame-by-frame stepping, playback speed controls, and repeat modes (off, loop one, loop all).
- **Clip Queue**: Quick slide-out drawer (`Q`) to switch to other clips in the folder without heading back to the library.

### Moments & Bookmarks
- **Mark Highlights**: Press `B` during playback to mark a moment at the current timestamp.
- **Timeline Markers**: Bookmarked moments appear directly as tick markers on the seek bar.
- **Moments Drawer**: Open the slide-out drawer to review marks, rename descriptions, or jump between timestamps.

---

## Keyboard & Mouse Shortcuts

| Input | Action |
| --- | --- |
| `Space` | Play / Pause |
| `Left` / `Right Arrow` | Seek backward / forward 5 seconds |
| `Shift + Left` / `Right Arrow` | Fine seek backward / forward 1 second |
| `Up` / `Down Arrow` | Volume up / down 5% |
| `Mouse Scroll Wheel` | Volume adjust (over canvas or volume slider) |
| `M` | Toggle mute |
| `B` | Mark moment bookmark at current timestamp |
| `Q` | Toggle Clip Queue drawer |
| `R` | Cycle repeat mode (Off / All / One) |
| `S` | Stop playback |
| `F` or `F11` | Toggle fullscreen |
| `Double Click` (Canvas) | Toggle fullscreen |
| `Escape` | Exit fullscreen / Return to library |

---

## Tech Stack

- **UI Framework**: WinUI 3 (Windows App SDK 1.6+) unpackaged
- **Runtime**: .NET 8 (`net8.0-windows10.0.26100.0`)
- **Architecture**: MVVM via `CommunityToolkit.Mvvm` with dependency injection
- **Video Engine**: `LibVLCSharp.WinUI` and native VideoLAN LibVLC 3.0 (Direct3D11 SwapChainPanel)
- **Thumbnails**: Windows Shell `StorageItemThumbnail` with persistent disk caching
- **Storage**: JSON storage for user settings, watched folders, and clip metadata

---

## Getting Started

### Prerequisites
- Windows 10 (version 1809 or newer) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer)

### Build and Run

1. Clone the repository:
   ```bash
   git clone https://github.com/k25jura/Reeled.git
   cd Reeled
   ```

2. Build the solution:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run --project Reeled\Reeled.csproj
   ```

---

## License

This project is licensed under the GPL-3.0 license. See the [LICENSE](LICENSE) file for details.
