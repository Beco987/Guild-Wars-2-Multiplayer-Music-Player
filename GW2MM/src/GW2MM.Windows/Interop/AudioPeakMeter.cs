using System.Runtime.InteropServices;

namespace GW2MM.Windows.Interop;

/// <summary>Peak level of the default playback device (Core Audio IAudioMeterInformation), replacing VA.ahk.</summary>
internal sealed class AudioPeakMeter : IDisposable
{
    private const int eRender = 0;
    private const int eConsole = 0;
    private const int CLSCTX_ALL = 23;
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IAudioMeterInformation? _meter;

    private AudioPeakMeter(IAudioMeterInformation meter) => _meter = meter;

    public static AudioPeakMeter OpenDefaultPlayback()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(eRender, eConsole, out var device));
            try
            {
                var iid = IID_IAudioMeterInformation;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj));
                return new AudioPeakMeter((IAudioMeterInformation)obj);
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public float Peak()
    {
        if (_meter is null) throw new ObjectDisposedException(nameof(AudioPeakMeter));
        Marshal.ThrowExceptionForHR(_meter.GetPeakValue(out var peak));
        return peak;
    }

    public void Dispose()
    {
        if (_meter is not null) Marshal.ReleaseComObject(_meter);
        _meter = null;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
