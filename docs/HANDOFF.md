# Aero Control — handoff (2026-11-25, 22:30)

Replacement UI for GIGABYTE Control Center (GCC) on the user's **AERO 14 OLED BMF** (i7-13620H, RTX 4050,
2880×1800 90/60 Hz OLED, white backlit keyboard). macOS-styled WPF app, .NET Framework 4.8, runs elevated.

## ⚠ Current state — read first

**The internal keyboard is dead until a power cycle.** At 22:25 the app set the keyboard backlight to 100%
(mode switch Turbo → Silent) and immediately sent a status query (feature report 0x88). The query timed out
(5 s) and the whole keyboard stopped responding. `pnputil /restart-device USB\VID_0414&PID_7A3A\AP0000000003`
made it drop off the bus (all `VID_0414&PID_7A3A` nodes "not present"); `pnputil /scan-devices` did not bring
it back. User was told to: Restart → if still dead, full Shut down 10–15 s → if still dead, unplug charger and
hold power 30 s (EC reset). Only volatile state was touched (lighting feature reports, same commands GCC
sends); no firmware/flash writes — expected to recover.

**First thing next session: ask whether the keyboard works after the restart.**

Startup state right now:
- `AeroControl` logon task: **DISABLED** (so it can't re-hang the keyboard at sign-in). Re-enable only after the fix below is tested: `schtasks /Change /TN AeroControl /ENABLE`.
- GCC logon task `GCC`: **DISABLED** (by design; GCC stays installed — its `CPUOC.dll`/`YccDrvv3.dll` are used for CPU power limits). Undo: `schtasks /Change /TN GCC /ENABLE`.
- App is not running (killed during recovery).

## Fix to do before re-enabling startup (keyboard HID safety)

In `src/AeroControl/Hardware/KeyboardLight.cs` + `Core/Controller.cs`:
1. Sleep **65 ms after every SetFeature**, exactly as GCC's `IteKeyBoard.SetLightEffect_Kb` does.
2. **No read-back right after a write** (remove `_kb.GetBrightness()` from the readback log line in
   `Controller.ApplyMode` and from `ReadKeyboard` after Fn+Space — the key report already carries the level).
3. **Circuit breaker**: if any HID call fails or takes > ~500 ms, stop all keyboard HID traffic for the rest of
   the session and log it (never retry in a loop).
4. Serialize keyboard I/O (already on the single `hw` worker — keep it that way; Fn-key path must not call HID
   from another thread).
Test with the user watching: mode switches with kb-follow on, Fn+Space spam, then re-enable the logon task.

## What works (verified on hardware, GCC closed)

| Area | Notes |
|---|---|
| 4 modes (Silent/Balanced/Performance/Turbo) | fans + CPU PL (read back from MSR 0x610: 15/30, 40/70, DC Balanced 35/45 W) + Windows power mode + per-mode kb level |
| Plug/unplug auto switch | AC/DC mode, 90/60 Hz, optional 1920×1200 on battery |
| Refresh rate, white point, brightness | brightness must use power-plan VIDEONORMALLEVEL (WmiSetBrightness has no effect on this OLED) |
| Charge limit | 100/80/60 via SetChargePolicy + SetChargeStop |
| OLED Care | dark mode, taskbar auto-hide/transparency, pixel refresh, ASC fade 70%→35% (GCC's exact behaviour), pixel shift |
| Fn keys (commit c52af7c) | Fn+Esc `0x84` cycles Silent→Balanced→Performance (Turbo is app-only, per user); Fn+F3/F4 `0x7D/0x7E` brightness ±10 with HUD; Fn+F10 `0x81` touchpad HUD (state from `HKCU\...\PrecisionTouchPad\Status\Enabled`); Fn+Space `04-01-lvl` levels 0/24/32/50 → off/48/64/100% HUD. Mic/Fn-lock/Win-lock mapped from GCC code but not present on this keyboard |
| Icon | user's `Desktop\appicon.png` logo rebuilt as vector, plain white (`tools/make-icon.ps1`) |

Not yet tested: fan-curve editor write.
Open question to user: **what should Fn+F12 (`0x88`) do?** (GCC never handled it.) Options offered: cycle
Windows power mode / same as Fn+Esc / nothing.
Removed by user request: battery cycle count (firmware `getBattCyc1`=92 is unverified; Windows reports 0) — don't re-add.

## Layout

- `src/AeroControl/` — app. `Hardware/` (GbWmi firmware calls, Fans, Battery, Sensors, CpuPower via GCC's CPUOC.dll, KeyboardLight HID, WindowsPowerMode), `Display/` (Panel refresh/resolution/brightness, OledCare, ColorTemperature), `Input/` (HotkeyListener raw-input + GB_WMIACPI_Event, Microphone), `Core/` (Controller worker queue, Modes, Settings, GccSync, Startup, FnKeys), `Views/`, `ViewModels/`, `Themes/`.
- `tools/install.ps1` — build Release + install to `C:\Program Files\AeroControl` + relaunch (run elevated).
- `tools/make-icon.ps1` — regenerate icon.
- `re/` — decompiled GCC (gitignored, reference only; never commit).
- Settings/log: `%LOCALAPPDATA%\AeroControl\settings.json`, `aerocontrol.log` (every hardware write + readback + raw Fn key events are logged).
- Diagnostics: `AeroControl.exe --probe <file>` (read-only firmware/HID/display survey), `--shot <dir>` (render pages to PNG), env `AEROCONTROL_FASTOLED=1` (30× faster idle timers).

## Hardware facts (from probes)

- Firmware: `root\WMI` `GB_WMIACPI_Get/Set` (admin only). RPM from `getRpm1/2` is byte-swapped. Fn-lock/boot-sound/storage-LED getters error on this model.
- Keyboard: USB HID `0414:7A3A`, ITE all-white, 9-byte feature reports `[0, cmd, a1..a6, 255-sum]`; `0x08` set effect (brightness = %/2), `0x88` query, `0x0A` 1-min auto-off. Fn keys arrive as 4-byte input reports on usage page 0xFF02/usage 1.
- Design mockup (approved): https://claude.ai/artifact/FvXKPnGZZrHtRPxDhYVfVf
