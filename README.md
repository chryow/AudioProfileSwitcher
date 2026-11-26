# AudioProfileSwitcher

A lightweight, dark-mode Windows 10/11 desktop & tray utility for switching **output + input (+ optional communications output)** audio device pairs and volumes in one click or global hotkey.

Built so you can walk away from your desk and switch between speakers, wireless headsets, and microphones instantaneously without digging through Windows Sound settings.

## Highlights in v1.2.0

- **One-click & hotkey Profile Cards** — Pair an Output device, an Input microphone, an optional **Voice / Communications Output** (Discord, Zoom, Teams), and optional per-card **Output / Input volumes (0–100%)**.
- **Event-driven CoreAudio** — Subscribes directly to Windows CoreAudio endpoint, default-role, state, and mute notifications (coalesced at 120 ms on the UI thread with a lightweight 45 s safety check) and skips redundant `SetAsDefault` / `SetAsDefaultCommunications` COM calls.
- **Smart Auto-Activate & Disconnect Fallbacks** — Automatically switch to a card when its headset/mic connects (with debounce & flapping protection), and fall back to **None**, **Previous Card**, or a **Specific Card** when the active card's device disconnects.
- **Resilient Name Relinking** — Cards and Cycle Output / Cycle Input filters remember human device names alongside endpoint GUIDs, automatically relinking when Windows reassigns endpoint IDs after driver updates or USB port changes.
- **Distinct Audio Cues** — Click-free continuous-phase synthesized WAV cues for **Switch** (`E5 → A5`), **Mic Muted** (`A5 → D5`), and **Mic Unmuted** (`E5 → B5`), preloaded in memory for instant non-blocking playback.
- **Per-Monitor DPI Overlays** — Click-through OSD banner on your active monitor plus a configurable **MIC MUTED** indicator (`Top center`, `Top right`, `Bottom right`, or `Active monitor top`).
- **Fast Workflow QoL** — Global hotkeys + `Ctrl + Shift + 1–9` direct card jump slots, right-click card context menu (`Activate`, `Edit`, `Duplicate`, `Use as Fallback`, `Delete`), tray quick-switch Output/Input submenus with shortcut hints, saved window position, and `Esc` to close Settings or hide to tray.

## Download & Run

Each [GitHub Release](https://github.com/chryow/AudioProfileSwitcher/releases) includes **two** single-file Windows x64 executables — pick whichever fits best:

| File | Size | What's inside | Best for |
| :--- | :--- | :--- | :--- |
| **`AudioProfileSwitcher.exe`** | ~430 KB | Lightweight (**framework-dependent**) — contains only the app; uses Windows' shared [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (prompts to install if missing). | Fast download & minimal disk footprint when .NET 8 is installed. |
| **`AudioProfileSwitcher-standalone.exe`** | ~70 MB | **Self-contained** — bundles the entire .NET 8 Desktop Runtime inside the `.exe` with zero prerequisites. | Plug-and-play on any fresh Windows 10/11 PC with nothing installed. |

No installer or administrator privileges required. Settings and rolling logs are stored in `%AppData%\AudioProfileSwitcher\` (`config.json` and `app.log`) and can be opened directly from **Settings → Open Data Folder / Open Log File**.

## Build, Test & Publish

```bash
# Build solution (warnings as errors)
dotnet build AudioProfileSwitcher.sln -c Release -warnaserror

# Run unit tests
dotnet test

# Publish lightweight framework-dependent single-file EXE (~430 KB)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish

# Publish self-contained standalone single-file EXE (~70 MB, includes .NET 8 runtime)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```
