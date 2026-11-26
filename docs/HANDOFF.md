# Aero Control — handoff (2026-11-25, 23:00)

Replacement UI for GIGABYTE Control Center (GCC) on the user's **AERO 14 OLED BMF** (i7-13620H, RTX 4050,
2880×1800 90/60 Hz OLED, white backlit keyboard). macOS-styled WPF app, .NET Framework 4.8, runs elevated.

## Current state

Keyboard recovered after the reboot (22:35). The incident at 22:25: after slider drags (writes only), a mode
switch wrote the kb level then immediately queried 0x88. The query hung 5 s and the keyboard dropped off USB.
**Fixed in ec19a5a** (`Hardware/KeyboardLight.cs`): 65 ms settle after every SetFeature (as GCC does), queries
refused within 1.5 s of a write, circuit breaker (any failed or >500 ms HID call disables kb HID for the session,
logged as `KEYBOARD HID DISABLED`). Mode switch no longer reads kb back; Fn+Space uses the level in its report;
slider writes are logged. Verified: `--kbtest` (incident pattern x6), `--kbsweep` (~100 writes), and the user
dragging the slider, switching modes and pressing Fn+Space in the app: no trips.

Startup: release installed and `AeroControl` logon task re-registered at 22:44 (`tools/install.ps1 -Startup`,
or `AeroControl.exe --startup on|off`). GCC's `GCC` logon task stays DISABLED (undo: `schtasks /Change /TN GCC /ENABLE`).

## Since 22:45 (user approved)

- Volume keys (05bde07): `Input/MediaKeyHook.cs` low-level hook swallows VK 0xAD/0xAE/0xAF so the Windows flyout
  never shows; `Speakers` (Microphone.cs) steps 2%, up unmutes. User confirmed: no Windows popup.
- Fn popup (bf83e4d): `Views/OsdWindow.cs` is now a macOS-style card in the top-right corner, slides in from the
  right. User liked it. `--shot` also renders `osd-level.png` / `osd-state.png`.
- Airplane key: left to Windows (radio HID collection, no interceptable key; disabling the device node is risky).
- Fn+F12: GCC toggled AI Boost (per-app automatic profiles). User: skip for now. Idea if revisited: "Auto" mode.

## 2026-11-26 02:40 — design review pass (user approved list)

Content capped at 720 px; thin overlay scrollbar (Theme.xaml ScrollViewer/ScrollBar); sidebar dot → logo "A" path;
"Options ›" links in accent (LinkButton now draws its own TextBlock: the implicit TextBlock style was overriding the colour);
"In this mode" heading; temperature bars amber/red (CPU 85/95 °C, GPU 78/85 °C); Battery page: maximum capacity bar
(design vs full-charge from root\wmi BatteryStaticData/BatteryFullChargedCapacity, readable unelevated: 63.0 of 63.0 Wh),
wear, charge/discharge watts; pixel-shift copy says icons only; keyboard page draws the real 14" layout (`Views/KeyboardPreview.cs`).

**Custom fan curve now actually used** (before, no mode turned it on, so drags did nothing): per-mode switch
`Settings.CustomCurve[4]`, `Controller.FanFor(mode)`; one shared curve; floor 20% (fan-off zone deliberately not offered,
untested); live "Now xx °C" dot. Verified with `--curvetest` (elevated): flat 60% → duty 60/60%, ~5,270 rpm;
flat 30% → duty 30/30%, ~2,800 rpm; then the mode's own fans restored. PASS.
`--shot` now runs beside a running instance (no mutex, no keyboard HID, no Fn keys); run unelevated with
`__COMPAT_LAYER=RunAsInvoker` to skip UAC (sensors then show "–"). Also renders `main-perf-curve.png`.

Fan-off zone: built, hardware-tested (EC stops at 0%, restarts ~1 s after the CPU crosses the threshold, has its own
~6 °C hysteresis) and then REMOVED at the user's request ("20% is virtually silent anyway"). Don't re-add.
Code is in git history (a2a45fd, b14ac96, reverted). Eco measured: 25% idle, 31% from ~55 °C.

Later 2026-11-26: Settings icon = flat-top hex nut; tray popover's button = app logo (`LogoPath` in Icons.xaml,
shared with the sidebar); global dark ToolTip style (tooltips were white text on white).
Elevated installs from a script: don't use `Start-Process -Wait` on install.ps1 — it waits for the relaunched
AeroControl too and never returns; use `-PassThru` and `.WaitForExit()`.

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

Fn+F12 (`0x88`): GCC used it for AI Boost on/off; user said skip for now. Options offered: cycle
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
