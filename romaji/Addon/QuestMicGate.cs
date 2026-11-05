using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Valve.VR;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

/// <summary>Left-hand A/X is a temporary input gate, independent of the user's mic switch.</summary>
public static class QuestMicGate
{
    private static int pressed;
    private static long sampledAt;
    private static readonly ConditionalWeakTable<object, Source> Sources = new();
    private sealed record Source(bool IsMic);
    public static bool IsMuted => Volatile.Read(ref pressed) != 0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref sampledAt)).TotalMilliseconds < 300;

    // Called only on the application's existing OpenVR event-loop thread, while it
    // is in the active placement/input phase. Never access OpenVR from audio workers.
    public static void PollOnVrLoop()
    {
        var down = false;
        try
        {
            var system = OpenVR.System;
            if (system is not null)
            {
                var left = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                var state = new VRControllerState_t();
                down = left != OpenVR.k_unTrackedDeviceIndexInvalid && system.IsTrackedDeviceConnected(left)
                    && system.GetControllerState(left, ref state, (uint)Marshal.SizeOf<VRControllerState_t>())
                    && IsXPressed(state.ulButtonPressed);
            }
        }
        catch { }
        Publish(down);
    }

    public static void Reset() => Publish(false);
    public static bool IsXPressed(ulong buttons) => (buttons & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
    private static void Publish(bool down)
    {
        Interlocked.Exchange(ref sampledAt, Stopwatch.GetTimestamp());
        Volatile.Write(ref pressed, down ? 1 : 0);
    }

    public static AudioDataEventArgs Filter(AudioDataEventArgs audio, object pipeline)
    {
        var source = Sources.GetValue(pipeline, p => new Source(string.Equals(
            p.GetType().GetField("_sourceName", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(p) as string,
            "Mic", StringComparison.OrdinalIgnoreCase)));
        return FilterFrame(audio, source.IsMic, IsMuted);
    }

    public static AudioDataEventArgs FilterFrame(AudioDataEventArgs audio, bool isMic, bool muted)
    {
        // Feed silence so VAD can finish pre-press speech normally. Never retain the muted
        // samples, alter the shared capture buffer, or change the manual recognition switch.
        if (!isMic || !muted || audio.AudioData.Length == 0) return audio;
        return new AudioDataEventArgs(new float[audio.AudioData.Length]);
    }
}
