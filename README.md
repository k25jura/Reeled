# Reeled 🎬

**Reeled** is a modern Windows 11 gameplay clip player and manager built with **unpackaged WinUI 3 (Windows App SDK)** targeting **.NET 8** and powered by **LibVLCSharp** & native **VideoLAN LibVLC 3.0**.

Designed specifically for gamers to browse, index, manage, and review local MP4/MKV gameplay recordings (Shadowplay, OBS, Medal, etc.) with hardware-accelerated playback, real-time timeline scrubbing, and moment bookmarks.

---

## Features

### 🗂️ Library & Folder Navigation
- **Directory Tree Sidebar**: Browse configured watch folders and nested game subdirectories (e.g. `Captures/Valorant`, `Captures/Apex`).
- **Real-time Auto-Refresh**: Powered by `FileSystemWatcher`, newly saved or deleted clips update in the grid instantly.
- **Rich Clip Cards**: Displays video duration pills, resolution & framerate badges (`4K`, `1440p 60fps`, `1080p`), file size, and relative timestamps.
- **Search & Sort**: Instant search filtering and sorting by Newest, Oldest, Name A-Z, Duration, or File Size.
- **Clip Management**: Safe delete to Windows Recycle Bin, inline renaming, "Reveal in File Explorer", "Copy File Path", and favorite toggling.

### 🎥 Telegram-Style Media Playback
- **Immersive Theater Canvas**: Centered hardware-accelerated video view using `LibVLCSharp.WinUI` and `SharpDX Direct3D11`.
- **Autohiding Controls**: Top header and bottom floating control bar gracefully fade out on idle mouse movement.
- **Real-Time Scrubbing**: Smooth video seeking as you drag the slider thumb.
- **Timeline Moments & Bookmarks**: Press `B` or click **"Mark Moment"** to pin timestamps (e.g. "Clutch", "Ace"). Markers appear on the timeline and in a quick jump list.
- **Folder Playlist Drawer**: Slide-over queue sidebar to switch between clips in the folder without leaving the player.
- **Playback Speed**: Quickly switch speed (`0.5x`, `0.75x`, `1.0x`, `1.25x`, `1.5x`, `2.0x`).
- **HUD Toast Feedback**: Responsive visual pills for volume changes, speeds, and skip actions.

### ⌨️ Keyboard Shortcuts

| Key | Action |
| --- | --- |
| `Space` | Play / Pause toggle |
| `Left Arrow` / `Right Arrow` | Skip $\pm$5 seconds |
| `Shift + Left/Right Arrow` | Fine skip $\pm$1 second |
| `Up Arrow` / `Down Arrow` | Volume $\pm$5% |
| `M` | Toggle Mute |
| `B` | Mark timeline bookmark at current timestamp |
| `F` or `F11` | Toggle Fullscreen mode |
| `Escape` | Exit Fullscreen / Return to Library |

---

## Architecture

- **UI Framework**: WinUI 3 (Windows App SDK 2.4.0) unpackaged
- **Target Framework**: .NET 8 (`net8.0-windows10.0.26100.0`)
- **Pattern**: MVVM via `CommunityToolkit.Mvvm` (8.4.2)
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`
- **Video Rendering**: `LibVLCSharp.WinUI` (3.10.1) + `VideoLAN.LibVLC.Windows` (3.0.23.1) Direct3D11 SwapChainPanel
- **Metadata & Thumbnails**: Windows Shell `StorageItemThumbnail` with persistent disk caching
- **Recycle Bin Interop**: Native Win32 `shell32.dll` `SHFileOperation`

---

## Getting Started

### Prerequisites
- Windows 10 (1809+) or Windows 11
- .NET 8 SDK (or .NET 10 SDK)

### Build and Run
```bash
# Clone the repository
git clone https://github.com/k25jura/Reeled.git
cd Reeled

# Build unpackaged solution
dotnet build

# Run application
dotnet run --project Reeled\Reeled.csproj
```
