# A2DP Commander

A free Windows utility for managing Bluetooth audio profiles (A2DP/HFP). Solves the problem of incorrect automatic profile switching in Windows.

**Version:** 1.3.4 | **Languages:** Русский, English

---

## Table of Contents

- [The Problem](#the-problem)
- [The Solution](#the-solution)
- [New in 1.3.4](#new-in-134)
- [Experimental Features](#experimental-features)
- [Bluetooth Adapter Selection](#bluetooth-adapter-selection)
- [AAC Codec Issues on Intel Adapters](#aac-codec-issues-on-intel-adapters)
- [MMCSS Optimization for Reducing Stuttering](#mmcss-optimization-for-reducing-stuttering)
- [Important: Wait After Connection](#important-wait-after-connection)
- [System Requirements](#system-requirements)
- [Installation](#installation)
- [Building from Source](#building-from-source)
- [Usage](#usage)
- [Settings](#settings)
- [License](#license)
- [Authors](#authors)
- [Support the Project](#support-the-project)

---

## The Problem

When you connect Bluetooth headphones to Windows, the operating system creates **two separate audio devices**:

1. **A2DP (Advanced Audio Distribution Profile)** - High quality stereo audio
   - 44.1/48 kHz sample rate
   - Stereo sound
   - Supports high-quality codecs (AAC, aptX, LDAC)
   - **No microphone support**

2. **HFP (Hands-Free Profile)** - Low quality audio with microphone
   - 8/16 kHz sample rate
   - Mono sound
   - Uses basic CVSD/mSBC codec
   - **Includes microphone support**

### Why This Is a Problem

Windows automatically switches between these profiles, and it often makes the wrong decision:

- You start listening to music in high-quality A2DP mode
- An application requests microphone access (even if you don't need it)
- Windows silently switches to HFP mode
- Your music suddenly sounds terrible - muffled, mono, low quality
- You have no idea why this happened

This happens constantly with:
- Video conferencing apps (Zoom, Teams, Discord) that request mic access
- Games with voice chat features
- Any application that might potentially use a microphone
- Windows system sounds triggering unnecessary profile switches

---

## The Solution

A2DP Commander solves this problem by **disabling the HFP device in Windows Device Manager**. This forces Windows to use only the A2DP profile, ensuring consistently high audio quality.

### How It Works

1. The program detects your connected Bluetooth audio device
2. It identifies both the A2DP and HFP endpoints
3. Using Windows SetupAPI, it **programmatically disables the HFP device**
4. Windows can now only use A2DP, guaranteeing high-quality audio

### Two Operating Modes

| Mode | A2DP | HFP | Use Case |
|------|------|-----|----------|
| **Music** | Enabled | Disabled | Listening to music, watching videos |
| **Calls** | Enabled | Enabled | Voice calls, video conferencing |

You can switch between modes:
- Automatically based on running applications
- Manually via system tray menu

---

## New in 1.3.4

### Single Instance with Auto-Focus

- **When launched again**, the app automatically focuses the existing window
- No more "already running" popups — just focus on the existing window
- Implemented via Named Pipe IPC

### UI Improvements

- **Removed "Minimize to Tray" button** — closing the window does the same thing
- **External Encoder status** moved below codec settings with word wrap
- **Shortened texts** about adapter incompatibility for compactness
- **Version in title** updates automatically from Assembly

### Changelog

<details>
<summary>v1.2.1</summary>

- **Auto-detection of adapter support** — experimental features are automatically blocked if the BT adapter doesn't support LDAC/aptX HD
- **Clear error messages** — when trying to enable an unsupported feature, the reason is shown
</details>

<details>
<summary>v1.2.0</summary>

- **WiFi Coexistence** — disable Bluetooth/WiFi conflict on 2.4GHz
- **WiFi Power Saving** — disable WiFi power saving
- **Processing Period Control** — manage audio buffer size
- **Latency Query** — display audio latency in diagnostics
- **LDAC/aptX encoding** via external USB Bluetooth transmitter
- **Registry Codec Forcing** — change codec via registry
</details>

<details>
<summary>v1.1.0</summary>

- **Bluetooth Adapter Selection** — switch between multiple adapters
- **Codec Detection** — display current connection codec
</details>

---

## Experimental Features

### External Encoder

Allows using LDAC/aptX HD codecs via an **external USB Bluetooth transmitter**.

#### How It Works

```
Application → WASAPI capture → Encoder (Rust) → USB Transmitter → Headphones
```

#### Supported Codecs

| Codec | Bitrate | Quality |
|-------|---------|---------|
| LDAC | 990 / 660 / 330 kbps | Hi-Res |
| aptX HD | 576 kbps | High |
| aptX | 352 kbps | Good |
| SBC | 328 kbps | Basic |

#### Recommended USB Bluetooth Transmitters

| Device | Price | Codecs |
|--------|-------|--------|
| 1Mii B03Pro | ~$30 | aptX HD, LDAC |
| Avantree DG80 | ~$40 | aptX LL |
| FiiO BTA30 Pro | ~$70 | All codecs |
| Creative BT-W3 | ~$50 | aptX HD |

#### Requirements

- External USB Bluetooth transmitter with the desired codec support
- Virtual Audio Cable (VB-Audio) or similar (optional)

### Registry Codec Forcing

Changes Windows registry settings to force codec selection.

**Important:**
- Requires administrator rights
- Requires system restart
- Only works if your Bluetooth adapter supports the desired codec

---

## Bluetooth Adapter Selection

If you have multiple Bluetooth adapters in your system (e.g., built-in Intel and external USB Realtek), you can **switch between them** directly from the program.

### How It Works

1. Open the **Settings** tab
2. In the **Bluetooth Adapter** section, you'll see a list of all physical adapters
3. Select the desired adapter and click **Switch**
4. The program will disable all other adapters and enable the selected one

### Important Notes

- **Windows supports only one active Bluetooth adapter** at a time
- **Paired devices do NOT transfer** between adapters
- After switching, you'll need to **re-pair your headphones** with the new adapter
- A **computer restart** may be required

### Why Is This Useful?

Different adapters support different codecs:

| Adapter | SBC | AAC | aptX | aptX HD | LDAC |
|---------|-----|-----|------|---------|------|
| Intel (built-in) | ✓ | ✓* | ✗ | ✗ | ✗ |
| Realtek (USB) | ✓ | ✓ | ✓ | ✗ | ✗ |
| Creative BT-W5 | ✓ | ✓ | ✓ | ✓ | ✓ |

*AAC on Intel often has stuttering issues

---

## AAC Codec Issues on Intel Adapters

Many Intel Bluetooth adapters have problems with the AAC codec, causing:

- Audio stuttering and dropouts
- Crackling and popping sounds
- Intermittent connection issues
- Audio desynchronization

### The Fix

A2DP Commander allows you to **disable AAC codec via Windows Registry**:

**Registry Path:**
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters
```

**Value:**
```
BluetoothAacEnable = 0 (DWORD)
```

When AAC is disabled, Windows will fall back to SBC codec, which may have slightly lower quality but provides stable, stutter-free playback.

### IMPORTANT: Restart Required

**Changing the AAC setting requires a full computer restart to take effect.** The Bluetooth stack reads this registry value only during system startup. Simply reconnecting your headphones or restarting the Bluetooth service will NOT apply the change.

---

## MMCSS Optimization for Reducing Stuttering

A2DP Commander can apply system optimizations to improve audio quality through **MMCSS (Multimedia Class Scheduler Service)**.

### What This Optimization Does

1. **Disables network throttling during audio playback** (`NetworkThrottlingIndex = 0xFFFFFFFF`)
2. **Sets system priority to multimedia** (`SystemResponsiveness = 0`)
3. **Increases audio thread priority** (`Audio\Scheduling Category = High`)

### How to Enable

1. Open the **Settings** tab
2. Check **Optimize MMCSS for audio**
3. Click **Save**
4. **Restart your computer**

### When This Helps

- Audio stuttering during heavy network activity
- Interruptions when other applications are working
- General improvement in playback stability

---

## Important: Wait After Connection

After your Bluetooth device connects, **wait 3-5 seconds** before the automatic mode switch takes effect.

This delay is necessary because:
1. Windows needs time to fully enumerate the audio endpoints
2. The Bluetooth stack needs to complete the connection handshake
3. The audio drivers need to initialize both A2DP and HFP endpoints

If you try to play audio immediately after connection, you might experience a brief switch as A2DP Commander applies your preferred mode.

---

## System Requirements

### Operating System
- Windows 10 version 1903 (build 18362) or newer
- Windows 11 (any version)

### Runtime
- .NET 8 Desktop Runtime ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Hardware
- Bluetooth adapter (built-in or USB)
- Bluetooth headphones or speakers with A2DP support

### Permissions
- **Administrator rights** are required to enable/disable audio devices via SetupAPI

---

## Installation

### From Release (Recommended)

1. Download the latest release from the [Releases](https://github.com/Yumash/A2DP-Commander/releases) page
2. Choose your version:
   - `A2DP-Commander-vX.X.X-win-x64.zip` - Requires .NET 8 Runtime installed (~9 MB)
   - `A2DP-Commander-vX.X.X-win-x64-self-contained.zip` - Includes .NET Runtime (~66 MB)
3. Extract the archive to your preferred location
4. Run `A2DP-Commander.exe`
5. (Optional) Enable "Start with Windows" in settings

---

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 or Windows 11 (WPF requires Windows)
- Git

### Clone and Build

```bash
# Clone the repository
git clone https://github.com/Yumash/A2DP-Commander.git
cd A2DP-Commander

# Build (Debug)
dotnet build src/A2DPCommander/A2DPCommander.csproj

# Run
dotnet run --project src/A2DPCommander/A2DPCommander.csproj
```

### Release Build

```bash
# Standard release build
dotnet build src/A2DPCommander/A2DPCommander.csproj -c Release

# Self-contained single-file executable (includes .NET Runtime, ~66 MB)
dotnet publish src/A2DPCommander/A2DPCommander.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Framework-dependent single-file executable (requires .NET Runtime, ~9 MB)
dotnet publish src/A2DPCommander/A2DPCommander.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## Usage

### Main Interface

After launch, the program minimizes to the system tray. Available actions:

- **Left click** on icon - open main window
- **Right click** on icon - context menu with quick mode switching
- **Double click** - open main window

### Tray Icon

The icon changes based on state:
- Blue circle - "Music" mode, A2DP active
- Green circle - "Calls" mode, HFP active
- Gray circle - device not connected

### Automatic Switching

The program can automatically switch to "Calls" mode when certain applications are running:
- Zoom
- Microsoft Teams
- Discord
- Skype
- Google Meet
- And others (configurable list)

---

## Settings

Settings are stored in `settings.json` file in the same folder as the program.

### Main Parameters

| Parameter | Description |
|-----------|-------------|
| Bluetooth device | Bluetooth device to manage |
| Bluetooth adapter | Select active Bluetooth adapter (if multiple) |
| Default mode | Music or Calls on startup |
| Auto-start | Launch program on Windows startup |
| Notifications | Show notifications on mode switch |
| App-based switching | Switch mode when Zoom etc. starts |
| Disable Windows enhancements | Disable DSP effects for clean sound |
| Optimize MMCSS | System audio optimizations (requires restart) |

### Application Rules

You can configure which mode to use when specific applications are running. Rules have priorities - if multiple applications are running, the rule with highest priority applies.

---

## License

This project is licensed under the **MIT License**.

You are free to use, modify, and distribute this software. See the [LICENSE](LICENSE) file for details.

---

## Authors

- **Andrey Yumashev** - [github.com/Yumash](https://github.com/Yumash)
- **Claude** (Anthropic)

---

## Support the Project

If you find A2DP Commander useful, consider supporting the development:

**BTC:** `1BkYvFT8iBVG3GfTqkR2aBkABNkTrhYuja`

Any support helps develop the project and add new features!

---

**Thank you for using A2DP Commander!**
