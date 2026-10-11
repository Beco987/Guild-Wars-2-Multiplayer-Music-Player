using System.Diagnostics;
using System.Runtime.InteropServices;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Interop;

/// <summary>
/// The original Sync measurement: presses "1" (SendInput, like AHK's <c>Send, 1</c>) and times how long until the
/// default playback device's peak exceeds 0.001; 10 presses, 4 s apart.
/// Unlike the original, the test needs Guild Wars 2 to be open and brings it to the front before the first press
/// (the original pressed "1" in whatever window had focus, usually GW2MM itself after its "press OK" prompt).
/// A press is only sent while the game is the foreground window, and each press gives up after
/// <see cref="TimeoutMs"/> instead of waiting forever for a sound.
/// </summary>
internal sealed class AudioInputDelayMeter : IInputDelayMeter
{
    public const int Presses = 10;
    public const int TimeoutMs = 3000;
    public const int PauseMs = 4000;
    public const float Threshold = 0.001f;
    /// <summary>Time for the game to take keyboard focus before the first press.</summary>
    public const int FocusSettleMs = 300;

    public Task<IReadOnlyList<double>> MeasureAsync(CancellationToken ct) => Task.Run<IReadOnlyList<double>>(() =>
    {
        var game = GameWindow.Find();
        if (game == IntPtr.Zero)
            throw new InputDelayException("Guild Wars 2 is not open. Start the game, then run Sync again.");
        if (!GameWindow.Focus(game))
            throw new InputDelayException("Could not bring Guild Wars 2 to the front. Click the game window, then run Sync again.");
        Thread.Sleep(FocusSettleMs);

        var samples = new List<double>();
        using var meter = AudioPeakMeter.OpenDefaultPlayback();
        for (var i = 0; i < Presses; i++)
        {
            ct.ThrowIfCancellationRequested();
            // Never type "1" into another program if the user switched away during the test.
            if (NativeMethods.GetForegroundWindow() != game && !GameWindow.Focus(game))
            {
                if (!NativeMethods.IsWindow(game)) throw new InputDelayException("Guild Wars 2 was closed during the Sync test.");
                if (ct.WaitHandle.WaitOne(PauseMs)) ct.ThrowIfCancellationRequested();
                continue;
            }
            PressOne();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < TimeoutMs)
            {
                if (meter.Peak() > Threshold)
                {
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                    break;
                }
                Thread.Sleep(1);
            }
            if (ct.WaitHandle.WaitOne(PauseMs)) ct.ThrowIfCancellationRequested();
        }
        return samples;
    }, ct);

    private static void PressOne()
    {
        const ushort vk = 0x31;
        var sc = (ushort)NativeMethods.MapVirtualKeyW(vk, NativeMethods.MAPVK_VK_TO_VSC);
        var inputs = new[]
        {
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk, wScan = sc } } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, u = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk, wScan = sc, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } },
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
