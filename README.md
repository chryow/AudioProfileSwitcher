# AudioProfileSwitcher

A lightweight, dark-mode Windows 10/11 desktop & tray utility for switching **output + input (+ optional communications output)** audio device pairs and volumes in one click or global hotkey.

Built so you can walk away from your desk and switch between speakers, wireless headsets, and microphones instantaneously without digging through Windows Sound settings.

## Highlights in v1.5.0

- **Offline Voice Control (`System.Speech` + `NAudio.Wasapi`)** — Say `"Audio, switch to Headset"`, `"Audio, card 1"`, `"Audio, next card"`, `"Audio, mute mic"`, or `"Audio, show window"` completely offline. Includes a customizable wake phrase, confidence slider (`50–95%`), tray toggle, live `● VOICE` status pill, and a **Listening Microphone** picker so you can pin a USB/desk mic and keep Bluetooth headsets in full stereo quality.
- **Modern Obsidian & Indigo-Violet UI** — Layered gradient profile cards with glowing active accents, keycap shortcut slots (`1–9`), micro-pill device/volume badges, interactive live audio header (`● LIVE` / `● MUTED` click-to-mute), animated toggles, custom dark dropdowns, and Windows 11 rounded corners & tinted borders.
- **Smooth Floating Overlays** — Fade-animated glassy OSD switch toasts and an optional interactive/draggable `● MIC MUTED` pill (drag to any custom position or click to unmute, or stay 100% click-through for games).
- **Modern CoreAudio Architecture (`NAudio.Wasapi` + Native `IPolicyConfig` COM)** — Event-driven `IMMNotificationClient` & endpoint mute callbacks with System32-pinned P/Invokes and 100% `config.json` compatibility.
- **Power Workflow & Portability** — Global Show/Hide Window hotkey (`Ctrl + Shift + Space`), resizable window height (`440–1400 px`), custom dark checklist (`DarkCheckedList`), portable JSON profile Import/Export with automatic name relinking, and one-click GitHub Release update checking.

## Download & Run

Each [GitHub Release](https://github.com/chryow/AudioProfileSwitcher/releases) includes **two** single-file Windows x64 executables — pick whichever fits best:

| File | Size | What's inside | Best for |
| :--- | :--- | :--- | :--- |
| **`AudioProfileSwitcher.exe`** | ~700 KB | Lightweight (**framework-dependent**) — contains only the app; uses Windows' shared [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (prompts to install if missing). | Fast download & minimal disk footprint when .NET 8 is installed. |
| **`AudioProfileSwitcher-standalone.exe`** | ~69 MB | **Self-contained** — bundles the entire .NET 8 Desktop Runtime inside the `.exe` with zero prerequisites. | Plug-and-play on any fresh Windows 10/11 PC with nothing installed. |

No installer or administrator privileges required. Settings and rolling logs are stored in `%AppData%\AudioProfileSwitcher\` (`config.json` and `app.log`) and can be opened directly from **Settings → Open Data Folder / Open Log File**.

## Build, Test & Publish

```bash
# Build solution (warnings as errors, zero NU1701 suppressions)
dotnet build AudioProfileSwitcher.sln -c Release -warnaserror

# Run unit tests
dotnet test

# Publish lightweight framework-dependent single-file EXE (~700 KB)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish

# Publish self-contained standalone single-file EXE (~69 MB, includes .NET 8 runtime)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```
