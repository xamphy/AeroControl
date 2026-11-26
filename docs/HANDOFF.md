# Aero Control — handoff (2026-11-26, 04:00)

Replacement UI for GIGABYTE Control Center (GCC) on the user's **AERO 14 OLED BMF** (i7-13620H, RTX 4050,
2880×1800 90/60 Hz OLED, white backlit keyboard). macOS-styled WPF app, .NET Framework 4.8, runs elevated.
Public repo xamphy/AeroControl, branch `master`. Never add a Claude co-author line to commits (history was rewritten once).

## Current state

- **v0.2.0 released** (GitHub release `v0.2.0`, zip = `AeroControl.exe` + `.config` only, no GIGABYTE files).
  `master` pushed and in sync. README has 2× screenshots (`--shot <dir> --scale 2`) and a Diagnostics section.
- Installed: 0.2.0 in `C:\Program Files\AeroControl`, logon task `AeroControl` registered (highest, runs on battery).
  GCC's `GCC` logon task DISABLED (undo: `schtasks /Change /TN GCC /ENABLE`); GCC stays installed (CPUOC.dll needed).
- User's settings right now: Silent on AC, keyboard off, Follow-mode off, charge limit 60%, hibernate after 15 min on battery.
- User is often on an **external 1920×1080@60 monitor** (Sunshine also runs); Home showing 1080p/60 Hz is correct, not a bug.

## Done 2026-11-26 (all verified unless noted)

- **Design pass** (user-approved review list): 720 px content column, thin overlay scrollbar, logo "A" in the sidebar
  (`LogoPath` in Icons.xaml, also on the popover's corner button), hex-nut Settings icon (flat edge on top),
  accent "Options ›" links, "In this mode" heading, temperature bars amber/red (CPU 85/95 °C, GPU 78/85 °C),
  keyboard page draws the real 14" layout (`Views/KeyboardPreview.cs`), dark ToolTip style (were white on white),
  readable sidebar subtitle when selected, pixel-shift copy says icons only.
- **Custom fan curve actually used now** (no mode ever turned it on before): per-mode switch `Settings.CustomCurve[4]`,
  `Controller.FanFor(mode)`, one shared curve, 20% floor, live "Now xx °C" dot. `--curvetest`: flat 60% → duty 60%
  (~5,270 rpm), flat 30% → 30% (~2,800 rpm), restored. Popover mode tiles say "Custom" when it is on.
- **Home Usage card**: CPU/GPU/RAM from Task Manager's counters (`Hardware/Usage.cs`); RTX found once by DXGI LUID;
  never wakes the dGPU (no nvidia-smi/NVML). First counter read ~6 s, on its own thread.
- **Battery page**: maximum capacity + wear (design vs full charge, 63.0 of 63.0 Wh), charge/discharge watts,
  time left on battery / "Until 60%" while charging (hidden when held at the limit; **not yet seen on screen**),
  Sleep: hibernate after 15 min / 1 h / 3 h / never on battery (`Hardware/Hibernate.cs`, active plan's DC
  HIBERNATEIDLE; GCC had set 900 s; `--hibtest` round trip PASS, confirmed with powercfg).
- **Tray**: hover text "Silent · fans 25% | CPU 48° GPU 43° · 99% ⚡" (≤63 chars); popover fans tile = rpm,
  bottom row = fan %.
- **Diagnostics**: `--shot` runs beside the live app (no mutex, no kb HID, no Fn keys), renders full pages + light theme;
  `--sweep` (below); `tools/sweep-sheet.py`.

## Decided — don't re-add

- **Fan-off zone**: built, hardware-tested (EC stops at 0%, restarts ~1 s after the CPU crosses, own ~6 °C hysteresis),
  then removed by the user ("20% is virtually silent anyway"). Code in history (a2a45fd, b14ac96, reverted in cc4fd99).
- **Battery cycle count** (firmware 92 unverified, Windows reports 0).
- Silent keeps the EC's Eco profile (measured: 25% idle, 31% from ~55 °C; CPU ≤58 °C at 15 W).

## Open / ideas (not started)

- Keyboard power saving (GCC enables it for this model; check what it does in `re/` first).
- Keyboard slider sync from the firmware's `F7-00/F7-01` backlight events (currently logged as unmapped).
- Auto mode (per-app modes), which could give Fn+F12 (`0x88`, GCC's AI Boost) a job. User said skip F12 for now.
- Earlier kb "sweep didn't work" report — user said skip.
- Retake README Home shot on the built-in screen (2880×1800 90 Hz) if the user wants.

## How to work on it

- Build + install (elevated): `tools/install.ps1 [-Startup]`. From a script use `Start-Process ... -PassThru` +
  `.WaitForExit()` — **not** `-Wait`, which also waits for the relaunched app and never returns.
- Screenshots without UAC: `__COMPAT_LAYER=RunAsInvoker bin\Debug\net48\AeroControl.exe --shot <dir>` (sensors show "–");
  elevated for real values. `--scale 2` for README quality.
- **Settings sweep for logs**: `AeroControl.exe --sweep [dir]`, elevated, with the normal app CLOSED (one HID owner).
  Switches the 4 modes (9 s settle), kb 0/24/48/64/100, charge 100/80/60, screenshots each step, writes `sweep.log`
  (fans, duty, rpm, temps, PL from MSR, Windows mode, kb, charge), restores everything. Default output
  `%LOCALAPPDATA%\AeroControl\sweeps\<yyyyMMdd-HHmm>`; `python tools\sweep-sheet.py <dir>` → `sweep-sheet.png`.
  First run 03:28: all readbacks matched, no HID trips (Turbo 7,927/8,167 rpm).
- Hardware tests: `--curvetest`, `--hibtest`, `--kbtest`, `--kbsweep`, `--guardtest`/`--fanofftest` no longer exist (reverted).
- Release: bump `<Version>` in the csproj (About reads it), Release build, zip exe + config, tag `vX.Y.Z`, `gh release create`.

## What works (verified on hardware, GCC closed)

| Area | Notes |
|---|---|
| 4 modes (Silent/Balanced/Performance/Turbo) | fans + CPU PL (read back from MSR 0x610: 15/30, 40/70, DC Balanced 35/45 W) + Windows power mode + per-mode kb level |
| Plug/unplug auto switch | AC/DC mode, 90/60 Hz, optional 1920×1200 on battery |
| Refresh rate, white point, brightness | brightness must use power-plan VIDEONORMALLEVEL (WmiSetBrightness has no effect on this OLED) |
| Charge limit | 100/80/60 via SetChargePolicy + SetChargeStop |
| Custom fan curve | SetFanIndexValue ×15 + StepFanStatus=1; duty follows the table exactly |
| OLED Care | dark mode, taskbar auto-hide/transparency, pixel refresh, ASC fade 70%→35% (GCC's behaviour), pixel shift (icons only) |
| Keyboard | HID safety (ec19a5a): 65 ms settle, no query <1.5 s after a write, breaker logs `KEYBOARD HID DISABLED` |
| Fn keys | Fn+Esc `0x84` cycles Silent→Balanced→Performance (Turbo app-only); F3/F4 `0x7D/0x7E` brightness ±10; F10 `0x81` touchpad; Fn+Space `04-01-lvl` 0/24/32/50 → off/48/64/100%; volume keys taken from Windows (LL hook) with our popup; airplane key left to Windows |
| Hibernate after sleep | DC HIBERNATEIDLE read/write on the active plan |

## Layout

- `src/AeroControl/` — app. `Hardware/` (GbWmi, Fans, Battery, Sensors, Usage, Hibernate, CpuPower via GCC's CPUOC.dll,
  KeyboardLight HID, WindowsPowerMode), `Display/` (Panel, OledCare, ColorTemperature), `Input/` (HotkeyListener,
  MediaKeyHook, Microphone/Speakers), `Core/` (Controller worker queue, Modes, Settings, GccSync, Startup, FnKeys),
  `Views/` (FanCurveEditor, KeyboardPreview, OsdWindow, TrayIcon, Icon), `ViewModels/`, `Themes/` (Theme, Icons).
- `tools/install.ps1`, `tools/make-icon.ps1`, `tools/sweep-sheet.py`.
- `re/` — decompiled GCC (gitignored, reference only; never commit).
- Settings/log: `%LOCALAPPDATA%\AeroControl\settings.json`, `aerocontrol.log` (every hardware write + readback).

## Hardware facts (from probes)

- Firmware: `root\WMI` `GB_WMIACPI_Get/Set` (admin only). RPM from `getRpm1/2` is byte-swapped. Fn-lock/boot-sound/storage-LED getters error on this model. No MUX switch on this model (GCC doesn't enable it).
- Keyboard: USB HID `0414:7A3A`, ITE all-white, 9-byte feature reports `[0, cmd, a1..a6, 255-sum]`; `0x08` set effect (brightness = %/2), `0x88` query, `0x0A` 1-min auto-off. Fn keys arrive as 4-byte input reports on usage page 0xFF02/usage 1.
- GPU adapters: Intel iGPU, RTX 4050 (LUID …11a4c this boot; found by DXGI each start), and a virtual adapter.
- Design mockup (approved): https://claude.ai/artifact/FvXKPnGZZrHtRPxDhYVfVf
