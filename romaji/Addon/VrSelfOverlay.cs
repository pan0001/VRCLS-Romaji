using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Valve.VR;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

/// <summary>A real OpenVR overlay, parented to the existing panel; no desktop window is created.</summary>
public static class VrSelfOverlay
{
    public const int TextureWidth = 1200;
    public const int TextureHeight = 300;
    private static readonly ConditionalWeakTable<object, Panel> Panels = new();
    private sealed class Panel
    {
        public readonly object Gate = new();
        public ulong Handle, Parent;
        public string Original = "中文原文", Translation = "日文译文", Romaji = "等待你的发言…";
        public DateTime Timestamp = DateTime.MinValue;
        public long Revision, TextRevision, UploadedRevision = -1;
        public float Scale = 1;
        public DateTime ChangedAt = DateTime.UtcNow, LastTick, LastUpload;
        public bool Overflow;
        public Task<Frame>? PendingFrame;
        public DateTime RetryAfter;
        public bool MicMuted;
        public bool UploadPending;
        public long Submitted, Completed;
        public DateTime UploadStarted;
        public bool TimeoutLogged;
    }
    private sealed record Frame(byte[] Pixels, bool Overflow, long Revision);

    public static void OnLoopStep(object service, int step)
    {
        if (step == 2) QuestMicGate.PollOnVrLoop();
        if (step != 8) return;

        // Never execute native calls or draw while the application's state lock is held.
        if (Monitor.IsEntered(Read<object>(service, "_lock"))) return;
        Tick(service);
    }

    private static Frame DrawFrame(string romaji, string translation, string original, float scale, double seconds, long revision)
    {
        using var bitmap = Render(romaji, translation, original, scale, seconds, out var overflow);
        var data = bitmap.LockBits(new Rectangle(0, 0, TextureWidth, TextureHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[TextureWidth * TextureHeight * 4];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new Frame(pixels, overflow, revision);
        }
        finally { bitmap.UnlockBits(data); }
    }

    public static void Capture(SteamVrOverlayPanelState state, object service)
    {
        var panel = Panels.GetValue(service, _ => new Panel());
        var mic = state.Transcripts?.FirstOrDefault(t => string.Equals(t.Source, "Mic", StringComparison.OrdinalIgnoreCase));
        lock (panel.Gate)
        {
            var scale = Math.Clamp(state.FontScale, .7f, 1.4f);
            if (scale != panel.Scale) { panel.Scale = scale; panel.Revision++; }
            if (mic is null || mic.Timestamp < panel.Timestamp) return;
            var japanese = OverlayRomaji.IsJapanese(state.MicTargetLanguage) || OverlayRomaji.IsJapanese(state.MicTargetLanguage2) || OverlayRomaji.IsJapanese(state.MicTargetLanguage3);
            var translated = OverlayRomaji.IsJapanese(state.MicTargetLanguage) ? mic.Translation :
                OverlayRomaji.IsJapanese(state.MicTargetLanguage2) ? mic.Translation2 :
                OverlayRomaji.IsJapanese(state.MicTargetLanguage3) ? mic.Translation3 : mic.Translation;
            if (panel.Timestamp == mic.Timestamp && panel.Original == mic.Original && panel.Translation == translated) return;
            panel.Timestamp = mic.Timestamp;
            panel.Original = mic.Original;
            panel.Translation = translated;
            panel.Romaji = japanese ? "生成注音…" : "日语译文可显示罗马音";
            panel.ChangedAt = DateTime.UtcNow;
            panel.Revision++;
            var revision = ++panel.TextRevision;
            if (japanese) _ = CompleteReading(panel, revision, translated);
        }
    }
    private static async Task CompleteReading(Panel panel, long revision, string text)
    {
        var reading = await OverlayRomaji.ReadingAsync(text).ConfigureAwait(false);
        lock (panel.Gate)
        {
            if (revision != panel.TextRevision) return;
            panel.Romaji = reading ?? (string.IsNullOrWhiteSpace(text) ? "等待译文…" : "暂无可用注音");
            panel.Revision++;
            panel.ChangedAt = DateTime.UtcNow;
        }
    }

    private static T Read<T>(object service, string name) => (T)service.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;

    // Runs after the original frame, outside its state lock. Only CPU drawing runs on a worker;
    // OpenVR calls retain the app's event-loop context and lifecycle.
    public static void Tick(object service)
    {
        try
        {
            var panel = Panels.GetValue(service, _ => new Panel());
            lock (panel.Gate)
            {
                var now = DateTime.UtcNow;
                if (now < panel.RetryAfter || (now - panel.LastTick).TotalMilliseconds < 100) return;
                panel.LastTick = now;
                var muted = QuestMicGate.IsMuted;
                if (panel.MicMuted != muted) { panel.MicMuted = muted; panel.Revision++; }
                var overlay = OpenVR.Overlay;
                if (overlay is null) { panel.Handle = 0; panel.Parent = 0; return; }
                // SetOverlayRaw is asynchronous. Polling ImageLoaded/ImageFailed also lets
                // vrclient release each shared-memory upload. Never leave our overlay unpolled.
                if (panel.Handle != 0)
                {
                    var evt = new VREvent_t();
                    for (var i = 0; i < 64 && overlay.PollNextOverlayEvent(panel.Handle, ref evt, (uint)Marshal.SizeOf<VREvent_t>()); i++)
                    {
                        if (evt.eventType == (uint)EVREventType.VREvent_ImageLoaded || evt.eventType == (uint)EVREventType.VREvent_ImageFailed)
                        {
                            if (panel.UploadPending) panel.Completed++;
                            panel.UploadPending = false;
                            panel.TimeoutLogged = false;
                            if (evt.eventType == (uint)EVREventType.VREvent_ImageFailed)
                            {
                                panel.UploadedRevision = -1;
                                panel.RetryAfter = now.AddSeconds(3);
                            }
                        }
                    }
                    if (panel.UploadPending && !panel.TimeoutLogged && (now - panel.UploadStarted).TotalSeconds > 5)
                    {
                        panel.TimeoutLogged = true;
                        OverlayRomaji.LogFailure(new TimeoutException("Self overlay image completion missing; submissions paused."));
                    }
                }
                var parent = Read<ulong>(service, "_overlayHandle");
                if (Read<bool>(service, "_disposed") || !Read<bool>(service, "_desiredVisible") || parent == 0 || !overlay.IsOverlayVisible(parent))
                {
                    if (panel.Handle != 0) overlay.HideOverlay(panel.Handle);
                    return;
                }
                if (panel.Parent != parent && panel.Handle != 0) { overlay.DestroyOverlay(panel.Handle); panel.Handle = 0; }
                if (panel.Handle == 0)
                {
                    var key = $"vrcls.selfspeech.{Environment.ProcessId}.{parent}";
                    if (overlay.CreateOverlay(key, "VRCLS · 我的发言", ref panel.Handle) != EVROverlayError.None) return;
                    panel.Parent = parent;
                    panel.UploadedRevision = -1;
                    panel.UploadPending = false;
                    panel.TimeoutLogged = false;
                    overlay.SetOverlayInputMethod(panel.Handle, VROverlayInputMethod.None);
                    overlay.SetOverlayFlag(panel.Handle, VROverlayFlags.SortWithNonSceneOverlays, true);
                    overlay.SetOverlaySortOrder(panel.Handle, 1);
                    overlay.SetOverlayAlpha(panel.Handle, 1);
                }
                float parentWidth = .16f;
                if (overlay.GetOverlayWidthInMeters(parent, ref parentWidth) != EVROverlayError.None) return;
                var width = Math.Clamp(parentWidth * 1.35f, .18f, .65f);
                var relative = RelativeTransform(parentWidth, width);
                var transform = PlaceAbove(Read<HmdMatrix34_t>(service, "_panelTransformInDeviceSpace"), relative.m7, relative.m11);
                var absolute = Read<AppConfig>(service, "_config").SteamVrAnchorModeValue == SteamVrOverlayAnchorMode.Space;
                var transformError = absolute
                    ? overlay.SetOverlayTransformAbsolute(panel.Handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref transform)
                    : overlay.SetOverlayTransformTrackedDeviceRelative(panel.Handle, Read<uint>(service, "_panelTrackedDeviceIndex"), ref transform);
                if (transformError != EVROverlayError.None) { overlay.HideOverlay(panel.Handle); overlay.DestroyOverlay(panel.Handle); panel.Handle = 0; panel.RetryAfter = now.AddSeconds(3); return; }
                overlay.SetOverlayWidthInMeters(panel.Handle, width);
                if (!panel.UploadPending && panel.PendingFrame is { IsCompleted: true } pending)
                {
                    panel.PendingFrame = null;
                    var frame = pending.GetAwaiter().GetResult();
                    if (frame.Revision == panel.Revision)
                    {
                        var pinned = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
                        EVROverlayError error;
                        var started = Stopwatch.GetTimestamp();
                        try { error = overlay.SetOverlayRaw(panel.Handle, pinned.AddrOfPinnedObject(), TextureWidth, TextureHeight, 4); }
                        finally { pinned.Free(); }
                        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 100) panel.RetryAfter = now.AddSeconds(3);
                        if (error != EVROverlayError.None) { overlay.HideOverlay(panel.Handle); overlay.DestroyOverlay(panel.Handle); panel.Handle = 0; panel.RetryAfter = now.AddSeconds(3); return; }
                        panel.UploadedRevision = frame.Revision;
                        panel.UploadPending = true;
                        panel.UploadStarted = now;
                        panel.Submitted++;
                        panel.LastUpload = now;
                        panel.Overflow = frame.Overflow;
                    }
                }
                if (!panel.UploadPending && panel.PendingFrame is null && (panel.UploadedRevision != panel.Revision || panel.Overflow && (now - panel.LastUpload).TotalMilliseconds >= 250))
                {
                    var romaji = panel.Romaji; var translation = panel.Translation; var original = panel.Original;
                    var scale = panel.Scale; var seconds = (now - panel.ChangedAt).TotalSeconds; var revision = panel.Revision;
                    // One pending frame maximum: slow drawing never queues more work or blocks the VR loop.
                    panel.PendingFrame = Task.Run(() => DrawFrame(romaji, translation, original, scale, seconds, revision));
                }
                if (panel.UploadedRevision >= 0) overlay.ShowOverlay(panel.Handle);
            }
        }
        catch (Exception ex)
        {
            var panel = Panels.GetValue(service, _ => new Panel());
            panel.RetryAfter = DateTime.UtcNow.AddSeconds(5);
            OverlayRomaji.LogFailure(ex);
        }
    }

    public static HmdMatrix34_t RelativeTransform(float parentWidth, float width) => new()
    {
        m0 = 1, m5 = 1, m10 = 1,
        // Parent is 820x720; overlay origin is its center. Leave a physical gap above it.
        m7 = parentWidth * (720f / 820f) / 2 + width * (TextureHeight / (float)TextureWidth) / 2 + .012f,
        m11 = .001f
    };

    public static HmdMatrix34_t PlaceAbove(HmdMatrix34_t parent, float y, float z)
    {
        parent.m3 += parent.m1 * y + parent.m2 * z;
        parent.m7 += parent.m5 * y + parent.m6 * z;
        parent.m11 += parent.m9 * y + parent.m10 * z;
        return parent;
    }

    public static void Release(object service)
    {
        QuestMicGate.Reset();
        try
        {
            if (!Panels.TryGetValue(service, out var panel)) return;
            lock (panel.Gate)
            {
                if (panel.Handle != 0) OpenVR.Overlay?.DestroyOverlay(panel.Handle);
                panel.Handle = 0;
                panel.Parent = 0;
                panel.UploadPending = false;
                panel.UploadedRevision = -1;
                panel.PendingFrame = null;
            }
        }
        catch { }
    }

    public static (string Romaji, string Translation, string Original) GetText(object service)
    {
        var panel = Panels.GetValue(service, _ => new Panel());
        lock (panel.Gate) return (panel.Romaji, panel.Translation, panel.Original);
    }

    public static (long Submitted, long Completed, bool Pending) GetUploadState(object service)
    {
        var panel = Panels.GetValue(service, _ => new Panel());
        lock (panel.Gate) return (panel.Submitted, panel.Completed, panel.UploadPending);
    }

    public static Bitmap Render(string romaji, string translation, string original, float scale, double seconds, out bool overflow)
    {
        var bitmap = new Bitmap(TextureWidth, TextureHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        using var shape = new GraphicsPath();
        shape.AddArc(1, 1, 30, 30, 180, 90); shape.AddArc(TextureWidth - 32, 1, 30, 30, 270, 90);
        shape.AddArc(TextureWidth - 32, TextureHeight - 32, 30, 30, 0, 90); shape.AddArc(1, TextureHeight - 32, 30, 30, 90, 90); shape.CloseFigure();
        using var background = new SolidBrush(Color.FromArgb(244, 16, 23, 31));
        using var border = new Pen(Color.FromArgb(230, 137, 196, 172), 2);
        graphics.FillPath(background, shape); graphics.DrawPath(border, shape);
        using var titleFont = new Font("Microsoft YaHei UI", 21, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(200, 207, 216, 226));
        graphics.DrawString(QuestMicGate.IsMuted ? "我的发言 · 暂停收音" : "我的发言", titleFont, titleBrush, 28, 12);
        var sizes = new[] { 52f, 44f, 38f };
        var colors = new[] { Color.FromArgb(219, 255, 228), Color.White, Color.FromArgb(220, 228, 238) };
        var lines = new[] { romaji, translation, original };
        overflow = false;
        for (var i = 0; i < 3; i++)
        {
            using var font = new Font("Microsoft YaHei UI", sizes[i] * Math.Clamp(scale, .7f, 1.25f), FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(colors[i]);
            var row = new RectangleF(28, 49 + i * 79, TextureWidth - 56, 77);
            var clean = (lines[i] ?? "").Replace('\r', ' ').Replace('\n', ' ');
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            var textWidth = graphics.MeasureString(clean, font, int.MaxValue, format).Width;
            var extra = Math.Max(0, textWidth - row.Width);
            overflow |= extra > 1;
            var duration = extra / 42.0;
            var phase = extra > 0 ? seconds % (duration + 7) : 0;
            var offset = phase <= 4 ? 0 : Math.Min(extra, (phase - 4) * 42);
            var saved = graphics.Save();
            graphics.SetClip(row);
            graphics.DrawString(clean, font, brush, new RectangleF(row.X - (float)offset, row.Y, Math.Max(row.Width, textWidth) + 8, row.Height), format);
            graphics.Restore(saved);
        }
        return bitmap;
    }
}
