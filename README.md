# Aero Control

A macOS-styled replacement for GIGABYTE Control Center (GCC), built for the **GIGABYTE AERO 14 OLED (BMF)**.
WPF on .NET Framework 4.8, runs elevated, lives in the tray.

<p align="center"><img src="src/AeroControl/Assets/icon-preview.png" width="96" alt="Aero Control icon"></p>

<p align="center"><img src="docs/screenshots/home.png" width="820" alt="Home"></p>

## Features

- **Power modes**: Silent / Balanced / Performance / Turbo: fans, CPU power limits, Windows power mode and keyboard backlight per mode, separate modes on AC and battery with automatic switching
- **Display**: 90/60 Hz switching, optional lower resolution on battery, brightness, white point
- **Battery**: charge limit (100 / 80 / 60 %)
- **OLED care**: dark mode, taskbar auto-hide/transparency, pixel refresh, auto-dim when idle, pixel shift
- **Keyboard**: white backlight level, per-mode levels, auto-off
- **Fn keys** with macOS-style popups in the top-right corner: mode cycle (Fn+Esc), brightness, keyboard light, touchpad, microphone, and volume (replaces the Windows volume flyout)
- Custom fan curve editor

## Screenshots

| Tray popover | Fn-key popup |
|---|---|
| <img src="docs/screenshots/popover.png" width="300" alt="Tray popover"> | <img src="docs/screenshots/osd-level.png" width="380" alt="Volume popup"> |

| Performance | Display |
|---|---|
| <img src="docs/screenshots/perf.png" alt="Performance page"> | <img src="docs/screenshots/display.png" alt="Display page"> |

| Battery | Keyboard |
|---|---|
| <img src="docs/screenshots/battery.png" alt="Battery page"> | <img src="docs/screenshots/keyboard.png" alt="Keyboard page"> |

## Requirements

- GIGABYTE AERO 14 OLED BMF (other models are untested and may behave differently)
- Windows 10/11, .NET Framework 4.8
- **GIGABYTE Control Center installed** (it does not need to run): Aero Control loads GCC's own `CPUOC.dll` for CPU power limits. No GIGABYTE files are included in this repository.

## Build and install

From an elevated PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Startup
```

This builds a Release, installs it to `C:\Program Files\AeroControl`, registers a logon task (`-Startup`) and starts it in the tray.
To stop GCC from starting as well: `schtasks /Change /TN GCC /DISABLE` (undo with `/ENABLE`).

Settings and a log of every hardware write are in `%LOCALAPPDATA%\AeroControl`.

## Disclaimer

Unofficial and not affiliated with GIGABYTE. It talks directly to the laptop's firmware (WMI), the CPU's power-limit registers and the keyboard controller. Use at your own risk.
