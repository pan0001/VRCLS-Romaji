using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Kawazu;
using NMeCab.Specialized;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

/// <summary>Display-only annotation. Never modifies transcript data, OSC, or TTS.</summary>
public static class OverlayRomaji
{
    private const int Capacity = 512;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Task<string?>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static readonly ConditionalWeakTable<SteamVrOverlayTranscript, Languages> Routes = new();
    private static readonly SemaphoreSlim ConverterGate = new(1);
    private static readonly Lazy<MeCabIpaDicTagger> Converter = new(() =>
        MeCabIpaDicTagger.Create(Path.Combine(AppContext.BaseDirectory, "romaji-dic")));
    private static int errorLogged;
    private sealed record Languages(TranscriptLanguageMask Japanese);

    public static void Prepare(SteamVrOverlayPanelState? state, object? overlay)
    {
        try
        {
            if (state?.Transcripts is null) return;
            if (overlay is not null) VrSelfOverlay.Capture(state, overlay);
            var japanese = TranscriptLanguageMask.None;
            if (IsJapanese(state.MicTargetLanguage)) japanese |= TranscriptLanguageMask.Translation1;
            if (IsJapanese(state.MicTargetLanguage2)) japanese |= TranscriptLanguageMask.Translation2;
            if (IsJapanese(state.MicTargetLanguage3)) japanese |= TranscriptLanguageMask.Translation3;
            foreach (var item in state.Transcripts.Take(30))
            {
                if (item is null || !string.Equals(item.Source, "Mic", StringComparison.OrdinalIgnoreCase)) continue;
                // The app snapshots the list but retains each transcript object.
                var route = Routes.GetValue(item, _ => new Languages(japanese));
                Queue(item.Translation, route.Japanese, TranscriptLanguageMask.Translation1, overlay);
                Queue(item.Translation2, route.Japanese, TranscriptLanguageMask.Translation2, overlay);
                Queue(item.Translation3, route.Japanese, TranscriptLanguageMask.Translation3, overlay);
            }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    internal static bool IsJapanese(string? language) =>
        string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, "jp", StringComparison.OrdinalIgnoreCase) ||
        (language?.StartsWith("ja-", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void Queue(string? text, TranscriptLanguageMask japanese, TranscriptLanguageMask channel, object? overlay)
    {
        if ((japanese & channel) == 0 || string.IsNullOrWhiteSpace(text) || text.Length > 4096) return;
        if (!text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uff66' and <= '\uff9f')) return;
        lock (Gate)
        {
            if (Cache.TryGetValue(text, out var existing))
            {
                if (!existing.IsCompleted && overlay is not null)
                    _ = existing.ContinueWith(_ => Invalidate(new WeakReference<object>(overlay)), TaskScheduler.Default);
                return;
            }
            while (Cache.Count >= Capacity && Order.TryPeek(out var oldest))
            {
                if (!Cache[oldest].IsCompleted) return; // Bound pending work as well as stored results.
                Order.Dequeue();
                Cache.Remove(oldest);
            }
            var weakOverlay = overlay is null ? null : new WeakReference<object>(overlay);
            var task = Task.Run(() => ConvertAsync(text));
            Cache.Add(text, task);
            Order.Enqueue(text);
            _ = task.ContinueWith(_ => Invalidate(weakOverlay), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    internal static Task<string?> ReadingAsync(string text)
    {
        Queue(text, TranscriptLanguageMask.Translation1, TranscriptLanguageMask.Translation1, null);
        lock (Gate) return Cache.TryGetValue(text, out var task) ? task : Task.FromResult<string?>(null);
    }

    private static async Task<string?> ConvertAsync(string text)
    {
        await ConverterGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Use pronunciation, not spelling: particles は/へ/を are wa/e/o.
            // Join inflected endings before romanization so 行っ + て becomes itte.
            var nodes = Converter.Value.Parse(text);
            var groups = new List<string>();
            MeCabIpaDicNode? previous = null;
            foreach (var node in nodes)
            {
                var kana = node.Pronounciation;
                if (string.IsNullOrWhiteSpace(kana) || kana == "*") kana = node.Reading;
                if (string.IsNullOrWhiteSpace(kana) || kana == "*") kana = node.Surface;
                var join = previous is not null &&
                    ((node.PartsOfSpeech == "助動詞" && previous.PartsOfSpeech is "動詞" or "形容詞" or "助動詞") ||
                     (node.PartsOfSpeechSection1 == "接続助詞" && previous.PartsOfSpeech is "動詞" or "助動詞") ||
                     previous.Surface.EndsWith('っ') || previous.Surface.EndsWith('ッ') ||
                     (previous.Surface is "初め" or "はじめ" && node.Surface == "まして"));
                if (join && groups.Count > 0) groups[^1] += kana;
                else groups.Add(kana);
                previous = node;
            }
            var result = string.Join(" ", groups.Select(kana => Utilities.ToRawRomaji(kana, RomajiSystem.Hepburn)));
            result = Regex.Replace(result, @"\s+([。、！？,.!?])", "$1").Trim();
            return string.IsNullOrWhiteSpace(result) || result == text ? null : result;
        }
        catch (Exception ex) { LogFailure(ex); return null; }
        finally { ConverterGate.Release(); }
    }

    // Called only after the original method has assembled its normal subtitle blocks.
    public static (string Text, Color Color)[] Decorate(
        (string Text, Color Color)[] blocks, SteamVrOverlayTranscript item, TranscriptLanguageMask mask)
    {
        try
        {
            if (!string.Equals(item.Source, "Mic", StringComparison.OrdinalIgnoreCase) ||
                !Routes.TryGetValue(item, out var route)) return blocks;
            var output = new List<(string Text, Color Color)>(blocks.Length + 3);
            var index = 0;
            Add(item.Translation, TranscriptLanguageMask.Translation1);
            Add(item.Translation2, TranscriptLanguageMask.Translation2);
            Add(item.Translation3, TranscriptLanguageMask.Translation3);
            while (index < blocks.Length) output.Add(blocks[index++]);
            return output.Count == blocks.Length ? blocks : output.ToArray();

            void Add(string text, TranscriptLanguageMask channel)
            {
                if ((mask & channel) == 0 || string.IsNullOrWhiteSpace(text)) return;
                if (index >= blocks.Length || blocks[index].Text != text)
                    throw new InvalidOperationException("Unexpected subtitle block order");
                var block = blocks[index++];
                if ((route.Japanese & channel) != 0)
                {
                    Task<string?>? task;
                    lock (Gate) Cache.TryGetValue(text, out task);
                    if (task?.IsCompletedSuccessfully == true && task.Result is { Length: > 0 } reading)
                        output.Add((reading, Color.FromArgb(220, block.Color)));
                }
                output.Add(block);
            }
        }
        catch (Exception ex) { LogFailure(ex); return blocks; }
    }

    private static void Invalidate(WeakReference<object>? reference)
    {
        try
        {
            if (reference is null || !reference.TryGetTarget(out var overlay)) return;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = overlay.GetType();
            var sync = type.GetField("_lock", flags)?.GetValue(overlay);
            if (sync is null) return;
            lock (sync)
            {
                if (type.GetField("_disposed", flags)?.GetValue(overlay) is true) return;
                type.GetField("_panelDirty", flags)?.SetValue(overlay, true);
            }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    internal static void LogFailure(Exception ex)
    {
        if (Interlocked.Exchange(ref errorLogged, 1) != 0) return;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCLS", "logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "romaji.log"),
                $"{DateTimeOffset.Now:O} Romaji unavailable; original subtitles preserved. {ex.GetType().Name}{Environment.NewLine}");
        }
        catch { }
    }
}
