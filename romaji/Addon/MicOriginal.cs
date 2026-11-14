using VRCLS.Core.Models;

namespace VRCLS.Romaji;

public static class MicOriginal
{
    public static bool Applies(string? source) =>
        string.Equals(source, "Mic", StringComparison.OrdinalIgnoreCase) && MicOriginalSettings.Enabled;

    // The official ASR client chooses its transcription-only endpoint when there are zero targets.
    public static IReadOnlyList<string> RecognitionTargets(AppConfig config, string? source) =>
        Applies(source) ? Array.Empty<string>() : config.ResolveEnabledTargetLanguages(source);
}
