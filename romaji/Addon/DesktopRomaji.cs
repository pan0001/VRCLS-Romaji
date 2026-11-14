using System.Runtime.CompilerServices;
using System.Windows;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

public static class DesktopRomaji
{
    private static readonly ConditionalWeakTable<Window, SelfSpeechWindow> Windows = new();

    public static void Attach(Window window, AppConfig config)
    {
        QuestMicGate.UseConfig(config);
        MicOriginalSettings.Attach(window);
        Windows.GetValue(window, owner => new SelfSpeechWindow(owner, config));
    }
}
