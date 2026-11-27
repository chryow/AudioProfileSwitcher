# AudioProfileSwitcher

[![Latest Release](https://img.shields.io/github/v/release/chryow/AudioProfileSwitcher?style=flat-square&color=6366f1)](https://github.com/chryow/AudioProfileSwitcher/releases/latest)
[![Total Downloads](https://img.shields.io/github/downloads/chryow/AudioProfileSwitcher/total?style=flat-square&color=10b981)](https://github.com/chryow/AudioProfileSwitcher/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/chryow/AudioProfileSwitcher/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/chryow/AudioProfileSwitcher/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011%20(x64)-0078d4?style=flat-square)](https://github.com/chryow/AudioProfileSwitcher/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-a855f7?style=flat-square)](LICENSE)

**Switch your output, microphone, communications device, and volumes in 1 click, 1 global hotkey, or 1 offline voice command.**

Built so you can walk away from your desk and keep talking to your friends on Discord — flipping between desk speakers + XLR/USB mic and your wireless headset instantaneously without digging through Windows Sound settings.

<!--
  PREVIEW MEDIA SLOT
  To showcase the UI & live switching, drop a screenshot (`docs/preview.png`)
  and/or a short 10-second demo GIF (`docs/demo.gif`), then uncomment:

  ![AudioProfileSwitcher UI](docs/preview.png)
  ![One-click and voice switching demo](docs/demo.gif)
-->

---

## Why AudioProfileSwitcher?

| Feature | AudioProfileSwitcher | SoundSwitch | Windows Sound Settings |
| :--- | :---: | :---: | :---: |
| **Paired Output + Input + Comm Profile Cards** | ✅ One click / keybind | ⚠️ Separate hotkeys | ❌ Manual |
| **Per-Card Volume Presets** | ✅ Output & Mic | ❌ | ❌ |
| **Offline Voice Control (Pinned Mic)** | ✅ Keep Bluetooth stereo | ❌ | ❌ |
| **Interactive / Draggable `MIC MUTED` Pill** | ✅ Click-through or drag | ❌ | ❌ |
| **Auto-Activate & Disconnect Fallback** | ✅ Re-links renamed USB/BT | ⚠️ Basic | ❌ |
| **Zero-Install Portable Single `.exe`** | ✅ ~1.4 MB (or standalone) | ❌ Installer | Built-in |
| **Modern Dark Obsidian UI + Glassy OSD** | ✅ Native Win11 look | ⚠️ Classic | ⚠️ Multi-click |

---

## Key Features

- **Profile Cards (Output + Input + Communications + Volumes)** — Pair any combination of playback device, microphone, optional separate communications output, and target output/mic volumes into visual cards. Reorder cards, duplicate presets, or jump directly with `Ctrl + Shift + 1–9`.
- **Offline Voice Control (`System.Speech` + `NAudio.Wasapi`)** — Hands-free switching completely offline with zero cloud APIs. Pin a desk/USB microphone as your **Listening Microphone** so Bluetooth headsets stay in full A2DP stereo quality instead of dropping into hands-free mode.
- **Global Hotkeys & System Tray** — Rebindable global hotkeys for **Cycle Cards**, **Cycle Output**, **Cycle Input**, **Toggle Mic Mute**, and **Show / Hide Window** (`Ctrl + Shift + Space`), plus a fast right-click tray menu with direct device switching.
- **Smart Auto-Activate & Disconnect Fallback** — Automatically switch to a headset card when it powers on, fall back to your previous or designated speakers card when it disconnects, and auto-relink devices by name if Windows reassigns USB/Bluetooth GUIDs.
- **Smooth Floating Overlays & Audio Chimes** — Fade-animated glassy OSD switch toasts, synthesized switch/mute chimes, and a persistent `● MIC MUTED` overlay pill (click-through for games, or interactive to drag anywhere and click to unmute).
- **Native CoreAudio Architecture** — Powered by `NAudio.Wasapi` + native `IPolicyConfig` COM interop with lock-free `IMMNotificationClient` & endpoint mute callbacks, System32-pinned P/Invokes, portable JSON Import/Export, and built-in GitHub Release update checking.

---

## Voice Commands (Offline)

Enable **Settings → Voice Control**, choose your wake phrase (default: **`Audio`**), and speak naturally:

| Say this | Action |
| :--- | :--- |
| `"Audio, switch to <Card Name>"` | Activates the matching profile card |
| `"Audio, card 1"` … `"Audio, card 9"` | Activates card by slot number |
| `"Audio, next card"` | Cycles to the next profile card |
| `"Audio, next output"` / `"Audio, next mic"` | Cycles output or input devices |
| `"Audio, mute mic"` / `"Audio, unmute mic"` | Mutes or unmutes the default microphone |
| `"Audio, show window"` / `"Audio, hide window"` | Shows or hides AudioProfileSwitcher |

---

## Download & Run

Grab the latest release from **[GitHub Releases](https://github.com/chryow/AudioProfileSwitcher/releases/latest)** — each release includes **two** single-file Windows x64 executables:

| File | Size | What's inside | Best for |
| :--- | :--- | :--- | :--- |
| **`AudioProfileSwitcher.exe`** | ~1.4 MB | **Lightweight (framework-dependent)** — uses Windows' shared [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (prompts to install if missing). | Fast download & tiny footprint when .NET 8 is already installed. |
| **`AudioProfileSwitcher-standalone.exe`** | ~69 MB | **Standalone (self-contained)** — bundles the entire .NET 8 Desktop Runtime inside one `.exe`. | Plug-and-play on any fresh Windows 10/11 PC with zero installs. |

No installer or administrator privileges are required. Config (`config.json`) and rolling logs (`app.log`) are stored in `%AppData%\AudioProfileSwitcher\` and can be opened directly from **Settings → Open Data Folder / Open Log File**.

### Verifying Downloads & Windows SmartScreen

Because this is an independent, unsigned open-source `.exe`, Windows SmartScreen may show a *"Windows protected your PC"* warning on first launch (**More info → Run anyway**).

Every release built by [GitHub Actions CI](.github/workflows/ci.yml) includes a `SHA256SUMS.txt` file (and GitHub release digests) so you can verify your download in PowerShell:

```powershell
Get-FileHash .\AudioProfileSwitcher.exe -Algorithm SHA256
```

---

## Build, Test & Publish from Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (builds cleanly on Windows and Linux via `EnableWindowsTargeting`).

```bash
# Build solution (warnings treated as errors)
dotnet build AudioProfileSwitcher.sln -c Release -warnaserror

# Run unit tests
dotnet test

# Publish lightweight framework-dependent single-file EXE (~1.4 MB)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish

# Publish self-contained standalone single-file EXE (~69 MB, includes .NET 8 runtime)
dotnet publish AudioProfileSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```

---

## License

Released under the [MIT License](LICENSE).
