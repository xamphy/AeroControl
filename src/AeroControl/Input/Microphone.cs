using System;
using System.Runtime.InteropServices;

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

    private static IAudioEndpointVolume Endpoint()
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out var dev) != 0 || dev == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
            Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
            return (IAudioEndpointVolume)o;
        }
        catch (Exception ex) { Log.Write("Microphone: " + ex.Message); return null; }
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
    private interface IAudioEndpointVolume
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
