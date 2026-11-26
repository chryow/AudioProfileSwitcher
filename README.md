# AudioProfileSwitcher

A lightweight, dark-mode Windows 10/11 desktop & tray utility for switching **output + input (+ optional communications output)** audio device pairs and volumes in one click or global hotkey.

Built so you can walk away from your desk and switch between speakers, wireless headsets, and microphones instantaneously without digging through Windows Sound settings.

## Highlights in v1.3.0

- **Modern CoreAudio Architecture (`NAudio.Wasapi` + Native `IPolicyConfig` COM)** — Direct `IMMDeviceEnumerator` / `IMMNotificationClient` and `AudioEndpointVolume` event callbacks with deterministic COM cleanup, zero legacy `.NET Framework 4.0` shims, and 100% compatibility with existing `config.json` endpoint GUIDs.
- **Global Show / Hide Window Hotkey** — Pop the switcher window up or hide it to the tray from anywhere (default `Ctrl + Shift + Space`, rebindable or disableable with `Backspace`/`Delete` in Settings).
- **Interactive / Draggable `MIC MUTED` Pill** — Optional interactive mode lets you drag the `● MIC MUTED` overlay to any custom spot across monitors (saved as `Custom` position) or click it to unmute in one click, while keeping zero-interference click-through mode available for gaming.
- **Resizable Window Height & Custom Dark Checklist** — Drag the bottom grip to resize window height (`420–1400 px`, persisted across launches), and filter cycle devices in a custom-painted dark checklist (`DarkCheckedList`) with `Select All` / `Clear All`.
- **Portable Profile Import / Export & In-App Update Check** — Export/import your cards and cycle filters as portable `.json` bundles (automatically relinking device GUIDs by name on a new PC) and check GitHub Releases for updates in one click.
- **Profile Cards, Volumes & Distinct Audio Cues** — Optional separate **Voice / Communications Output** (`COMM` row), per-card **Output / Input volumes (0–100%)**, disconnect fallback modes (`None`, `Previous card`, `Specific card`), and click-free preloaded WAV cues (`Switch`, `Muted`, `Unmuted`).

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
