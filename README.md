# Reeled

> **Work in Progress (W.I.P.)**: Reeled is currently in active development. Features and UI components are subject to rapid iteration.

**Reeled** is a fast, modern Windows 11 gameplay clip player and library manager built with **unpackaged WinUI 3 (Windows App SDK)** targeting **.NET 8** and powered by **LibVLCSharp** and native **VideoLAN LibVLC 3.0**.

Designed specifically for gamers to browse, index, manage, and review local MP4 and MKV gameplay recordings (NVIDIA Shadowplay, OBS Studio, Medal, SteelSeries Moments, etc.) with hardware-accelerated playback, real-time timeline scrubbing, and moment bookmarking.

---

## Features

### Library and Folder Navigation
- **Directory Tree Sidebar**: Browse configured watch folders and nested game subdirectories (for example, `Captures/Valorant`, `Captures/Apex Legends`).
- **Watch Folder Management**: Configure multiple custom capture directories via Settings, with instant reset to the default Windows Captures folder.
- **Real-Time Auto-Refresh**: Powered by `FileSystemWatcher`, newly saved, renamed, or deleted clips update in the grid automatically.
- **Rich Clip Cards**: Visual preview cards displaying duration pills, resolution badges (4K, 1440p, 1080p, 60fps), file size, relative timestamps, and a dedicated yellow Moments count badge.
- **Search and Sort**: Instant search filtering and sorting by Newest, Oldest, Name (A-Z), Duration, or File Size.
- **Clip Context Menu**:
  - **Clip Information**: Dedicated modal displaying comprehensive file properties (resolution, framerate, duration, file size, container format, path, and bookmarked moments).
  - **Favorite Toggle**: Star and unstar clips with persistent metadata.
  - **Safe Delete**: Delete directly to the Windows Recycle Bin using native Win32 `SHFileOperation`.
  - **File Operations**: Inline renaming, Reveal in File Explorer, and Copy File Path.

### Hardware-Accelerated Playback
- **Direct3D11 Video Canvas**: Hardware-accelerated rendering using `LibVLCSharp.WinUI` Direct3D11 SwapChainPanel.
- **Real-Time Timeline Scrubbing**: Fluid, instantaneous video seeking while dragging the timeline slider.
- **Playback Speed**: Adjust speed on the fly with presets from 0.25x up to 2.0x.
- **Repeat Modes**: Cycle between Repeat Off, Repeat All, and Repeat One (`R`), with user preferences automatically saved across sessions.
- **Audio Control**: Volume slider with mouse scroll wheel support and a dynamic volume icon that reflects mute, low, medium, and high volume states.

### Moments and Bookmarking System
- **Mark Timestamps**: Press `B` or click **Mark Moment** during playback to pin key highlights (clutches, aces, funny moments).
- **Timeline Highlight Markers**: Pinned moments appear as prominent yellow tick markers directly along the timeline seek bar.
- **Jump to Moment**: Quick-jump buttons formatted with timestamps (`[ > Jump mm:ss ]`) to instantly leap to key points.
- **Moments Drawer**: Dedicated slide-out drawer to view, jump to, rename, or delete bookmarked moments.
- **Highlight Suggestion Card**: When opening a clip that contains saved moments, an unobtrusive Windows 11 card appears in the bottom corner offering immediate one-click jumps to bookmarked timestamps.

### Media Controls and Cinema Mode
- **Full-Width Control Bar**: Balanced, grouped control sections:
  - Playback controls: Stop, Previous Clip, Step Backward (-5s), Play/Pause, Step Forward (+5s), Next Clip.
  - Seek bar with elapsed and remaining time indicators and timeline moment highlights.
  - Tool buttons: Moments drawer toggle, Clip Queue drawer toggle, Playback speed selector, Repeat mode toggle.
  - Volume slider, mute toggle, and full-screen toggle.
- **Clip Queue Drawer**: Slide-in playlist sidebar (`Q`) allowing seamless switching between clips in the active folder without returning to the library.
- **Auto-Hiding Interface**: Header, window controls, and bottom control bar automatically fade out during playback inactivity.
- **Cursor Auto-Hide**: The Windows mouse cursor hides completely via native Win32 `ShowCursor` during playback and restores instantly upon pointer movement or keyboard input.

---

## Keyboard and Mouse Shortcuts

| Input | Action |
| --- | --- |
| `Space` | Play / Pause toggle |
| `Left Arrow` / `Right Arrow` | Skip backward / forward 5 seconds |
| `Shift + Left Arrow` / `Right Arrow` | Fine skip backward / forward 1 second |
| `Up Arrow` / `Down Arrow` | Volume up / down 5% |
| `Mouse Scroll Wheel` | Volume adjust (over player canvas or volume slider) |
| `M` | Toggle Mute |
| `B` | Mark a Moment bookmark at current timestamp |
| `Q` | Toggle Clip Queue drawer |
| `R` | Cycle Repeat mode (Off / All / One) |
| `S` | Stop playback |
| `F` or `F11` | Toggle Fullscreen mode |
| `Double Click` (Canvas) | Toggle Fullscreen mode |
| `Escape` | Exit Fullscreen / Return to Library |

---

## Architecture and Tech Stack

- **UI Framework**: WinUI 3 (Windows App SDK 1.6+) unpackaged
- **Target Framework**: .NET 8 (`net8.0-windows10.0.26100.0`)
- **Architecture Pattern**: MVVM via `CommunityToolkit.Mvvm` (8.4.2)
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`
- **Video Engine**: `LibVLCSharp.WinUI` (3.10.1) and native `VideoLAN.LibVLC.Windows` (3.0.23.1) Direct3D11 SwapChainPanel
- **Metadata and Thumbnails**: Windows Shell `StorageItemThumbnail` with persistent disk caching
- **Native Interop**:
  - `shell32.dll` `SHFileOperation` for shell Recycle Bin deletion
  - `user32.dll` `ShowCursor` and window subclassing for seamless cursor management
- **Persistence**: JSON-backed local storage for application settings, watch folders, and clip bookmarks

---

## Getting Started

### Prerequisites
- Windows 10 (version 1809 or newer) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer)
- Visual Studio 2022 or JetBrains Rider (with .NET desktop development workload and Windows App SDK)

### Build and Run

1. Clone the repository:
   ```bash
   git clone https://github.com/k25jura/Reeled.git
   cd Reeled
   ```

2. Build the unpackaged solution:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run --project Reeled\Reeled.csproj
   ```

---

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
