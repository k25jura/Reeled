# Reeled

> **work in progress**: Reeled is currently developing. Features and UI are subject to change.

**Reeled** is a modern Windows 11 video viewer focused on game clips and library manager built with **unpackaged WinUI 3 (Windows App SDK)** targeting **.NET 8** and powered by **LibVLCSharp** and native **VideoLAN LibVLC 3.0**.

Designed specifically for those who wants to browse, index, manage, and review local MP4 and MKV gameplay recordings (NVIDIA Shadowplay, OBS Studio, Medal, SteelSeries Moments, etc.) in one place with hardware-accelerated playback, real-time timeline scrubbing, and moment bookmarking.

---

## Features

### Library and Folder Navigation
- **Directory Tree Sidebar**: Browse configured watch folders and nested subdirectories.
- **Folder Management**: Configure multiple custom watch directories.
- **Real-Time Auto-Refresh**: Powered by `FileSystemWatcher`, newly saved, renamed, or deleted clips update in the grid automatically.
- **Search and Sort**: Instant search filtering and sorting.
- **Clip Context Menu**:
  - **Clip Information**: Dedicated modal displaying comprehensive file properties.
  - **Favorite Toggle**: Star and unstar clips with persistent metadata.
  - **Safe Delete**: Delete directly to the Windows Recycle Bin.
  - **File Operations**: Inline renaming, Reveal in File Explorer, and Copy File Path.

### Hardware-Accelerated Playback
- **Direct3D11 Video Canvas**: Hardware-accelerated rendering using `LibVLCSharp.WinUI` Direct3D11 SwapChainPanel.
- **Real-Time Timeline Scrubbing**: Instantaneous video seeking while dragging the timeline slider.
- **Playback Speed**: Adjust the playback speed.
- **Repeat Modes**: Cycle between Repeat Off, Repeat All, and Repeat One (`R`), with user preferences automatically saved across sessions.
- **Audio Control**: Volume slider with mouse scroll wheel support and a dynamic volume icon that reflects mute, low, medium, and high volume states.

### Moments and Bookmarking System
- **Mark Timestamps**: Press `B` or click **Mark Moment** during playback to pin key highlights.
- **Timeline Highlight Markers**: Pinned moments appear as prominent yellow tick markers directly along the timeline seek bar.
- **Jump to Moment**: Quick-jump buttons formatted with timestamps to instantly see the key points.
- **Moments Drawer**: Dedicated slide-out drawer to view, jump to, rename, or delete bookmarked moments.

### Media Controls
- **Full-Width Control Bar**: Grouped, all usual playback controls such as Stop, Previous Clip, Step Backward, Play/Pause, Step Forward, Next Clip.
  - Seek bar with elapsed and remaining time indicators and timeline moment highlights.
  - Tool buttons: Moments drawer toggle, Clip Queue drawer toggle, Playback speed selector, Repeat mode toggle.
  - Volume slider, mute toggle, and full-screen toggle.
- **Clip Queue Drawer**: Slide-in playlist sidebar (`Q`) allowing switching between clips in the active folder without returning to the library.
- **Auto-Hiding Interface**: Header, window controls, bottom control bar and cursor automatically fade out during playback inactivity.

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

This project is licensed under the GPL-3.0 license. See the [LICENSE](LICENSE) file for details.
