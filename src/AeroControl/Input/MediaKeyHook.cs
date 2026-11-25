using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AeroControl.Input;

/// <summary>
/// Takes the volume keys (Fn+F5/F6/F7 and any keyboard's media keys) away from Windows so its volume flyout
/// does not appear; we change the volume ourselves and show our own popup.
/// Low-level keyboard hook: must be installed on a thread with a message loop (the UI thread), and the
/// callback must stay fast (Windows silently drops hooks that exceed LowLevelHooksTimeout), so it only
/// posts the key and returns. If the hook cannot be installed, Windows keeps handling the keys as before.
/// </summary>
public sealed class MediaKeyHook : IDisposable
{
    public const int VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF;

    /// <summary>Raised on key down (auto-repeat included) with the virtual-key code, on the hook thread.</summary>
    public event Action<int> Pressed;

    private readonly HookProc _proc; // keep the delegate alive
    private IntPtr _hook;

    public MediaKeyHook()
    {
        _proc = Callback;
        using var mod = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(mod.ModuleName), 0);
        Log.Write($"Volume keys: hook installed={_hook != IntPtr.Zero} (err {Marshal.GetLastWin32Error()})");
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == VolumeMute || vk == VolumeDown || vk == VolumeUp)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x100 || msg == 0x104) // WM_KEYDOWN / WM_SYSKEYDOWN
                    try { Pressed?.Invoke(vk); } catch { }
                return (IntPtr)1; // swallow down and up: Windows never sees the key
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
