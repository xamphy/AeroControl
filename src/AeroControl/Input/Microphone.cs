using System;
using System.Runtime.InteropServices;
using static AeroControl.Input.AudioEndpoint;

namespace AeroControl.Input;

/// <summary>
/// Default microphone mute, through Windows Core Audio (what GCC's OSDwindow did for the mic Fn key).
/// Minimal COM interop: IMMDeviceEnumerator → default capture IMMDevice → IAudioEndpointVolume.
/// </summary>
public static class Microphone
{
    public static bool? Toggle()
    {
        var vol = Endpoint();
        if (vol == null) return null;
        try
        {
            Marshal.ThrowExceptionForHR(vol.GetMute(out bool muted));
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(vol.SetMute(!muted, ref ctx));
            return !muted;
        }
        finally { Marshal.ReleaseComObject(vol); }
    }

    public static bool? IsMuted
    {
        get
        {
            var vol = Endpoint();
            if (vol == null) return null;
            try { return vol.GetMute(out bool m) == 0 ? m : (bool?)null; }
            finally { Marshal.ReleaseComObject(vol); }
        }
    }

    private static IAudioEndpointVolume Endpoint() => AudioEndpoint.Open(1 /* eCapture */);
}

/// <summary>Default speakers (render endpoint): what the volume keys change.</summary>
public static class Speakers
{
    /// <summary>Change volume by <paramref name="delta"/> (0..1 scale, Windows uses 0.02 per key press);
    /// raising it unmutes, like Windows. Returns (percent, muted) or null.</summary>
    public static (int percent, bool muted)? Step(float delta)
    {
        var vol = AudioEndpoint.Open(0 /* eRender */);
        if (vol == null) return null;
        try
        {
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevelScalar(out float v));
            v = Math.Max(0f, Math.Min(1f, (float)Math.Round((v + delta) * 50) / 50));
            Marshal.ThrowExceptionForHR(vol.SetMasterVolumeLevelScalar(v, ref ctx));
            if (delta > 0) vol.SetMute(false, ref ctx);
            vol.GetMute(out bool m);
            return ((int)Math.Round(v * 100), m);
        }
        catch (Exception ex) { Log.Write("Speakers: " + ex.Message); return null; }
        finally { Marshal.ReleaseComObject(vol); }
    }

    public static (int percent, bool muted)? ToggleMute()
    {
        var vol = AudioEndpoint.Open(0);
        if (vol == null) return null;
        try
        {
            var ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(vol.GetMute(out bool m));
            Marshal.ThrowExceptionForHR(vol.SetMute(!m, ref ctx));
            vol.GetMasterVolumeLevelScalar(out float v);
            return ((int)Math.Round(v * 100), !m);
        }
        catch (Exception ex) { Log.Write("Speakers: " + ex.Message); return null; }
        finally { Marshal.ReleaseComObject(vol); }
    }
}

internal static class AudioEndpoint
{
    public static IAudioEndpointVolume Open(int dataFlow)
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(dataFlow, 0 /* eConsole */, out var dev) != 0 || dev == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
            Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
            return (IAudioEndpointVolume)o;
        }
        catch (Exception ex) { Log.Write("Audio endpoint: " + ex.Message); return null; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
