using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

/// <summary>A persistent, three-line view independent of transcript-list scrolling and trimming.</summary>
internal sealed class SelfSpeechWindow : Window
{
    private readonly ScrollViewer contentScroll = new();
    private readonly Window owner;
    private readonly AppConfig config;
    private readonly TextBlock romaji = Line("SelfRomaji", 24, "#BFFFE0");
    private readonly TextBlock translation = Line("SelfTranslation", 22, "#FFFFFF");
    private readonly TextBlock original = Line("SelfOriginal", 19, "#CDD4DE");
    private INotifyCollectionChanged? collection;
    private INotifyPropertyChanged? viewModel;
    private INotifyPropertyChanged? latestNotifier;
    private object? latest;
    private bool closed;
    private bool movedByUser;
    private int revision;

    internal SelfSpeechWindow(Window owner, AppConfig config)
    {
        this.owner = owner;
        this.config = config;
        Title = "VRCLS · 我的发言";
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = 520;
        MaxHeight = 340;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;
        var title = Line("SelfTitle", 13, "#AFC7BF");
        title.Text = "我的发言";
        title.Cursor = Cursors.SizeAll;
        title.ToolTip = "拖动标题可移动小窗";
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            movedByUser = true;
            DragMove();
        };
        var rows = new StackPanel();
        foreach (var line in new[] { romaji, translation, original }) rows.Children.Add(line);
        contentScroll.Content = rows;
        contentScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        contentScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var layout = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);
        layout.Children.Add(contentScroll);
        Content = new Border
        {
            Child = layout, Background = new SolidColorBrush(Color.FromRgb(19, 25, 29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 121, 107)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 9, 16, 11)
        };
        romaji.FontWeight = FontWeights.SemiBold;
        romaji.Text = "等待你的发言…";
        SizeChanged += (_, _) => Position();
        owner.Loaded += OwnerLoaded;
        owner.DataContextChanged += ContextChanged;
        owner.LocationChanged += OwnerPositionChanged;
        owner.SizeChanged += OwnerSizeChanged;
        owner.StateChanged += OwnerStateChanged;
        owner.IsVisibleChanged += OwnerVisibilityChanged;
        owner.Closed += OwnerClosed;
    }

    private static TextBlock Line(string name, double size, string color) => new()
    {
        Name = name, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = size,
        Foreground = (Brush)new BrushConverter().ConvertFromString(color)!, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center
    };

    private void OwnerLoaded(object sender, RoutedEventArgs e)
    {
        if (closed) return;
        Owner = owner;
        ObserveContext();
        SyncVisibility();
    }
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        revision++;
        if (latestNotifier is not null) latestNotifier.PropertyChanged -= LatestChanged;
        latest = null;
        latestNotifier = null;
        romaji.Text = "等待你的发言…";
        translation.Text = original.Text = "";
        ObserveContext();
    }
    private void ObserveContext()
    {
        if (viewModel is not null) viewModel.PropertyChanged -= ViewModelChanged;
        if (collection is not null) collection.CollectionChanged -= CollectionChanged;
        viewModel = owner.DataContext as INotifyPropertyChanged;
        if (viewModel is not null) viewModel.PropertyChanged += ViewModelChanged;
        var items = owner.DataContext?.GetType().GetProperty("Transcripts")?.GetValue(owner.DataContext);
        collection = items as INotifyCollectionChanged;
        if (collection is not null) collection.CollectionChanged += CollectionChanged;
        if (items is IEnumerable all)
            foreach (var item in all) if (IsMic(item)) { Select(item); break; }
    }
    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Transcripts" or null or "") ObserveContext();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => CollectionChanged(sender, e))); return; }
        // Only new microphone entries replace the pinned content. Desktop messages, removal,
        // list trimming and list reset cannot replace it with an older or unrelated entry.
        if (closed || !ReferenceEquals(sender, collection)) return;
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace && e.NewItems is not null)
            foreach (var item in e.NewItems) if (IsMic(item)) { Select(item); break; }
    }
    private static string Get(object? item, string property) => item?.GetType().GetProperty(property)?.GetValue(item) as string ?? "";
    private static bool IsMic(object? item) => string.Equals(Get(item, "Source"), "Mic", StringComparison.OrdinalIgnoreCase);
    private void Select(object item)
    {
        if (ReferenceEquals(latest, item)) return;
        if (latestNotifier is not null) latestNotifier.PropertyChanged -= LatestChanged;
        latest = item;
        latestNotifier = item as INotifyPropertyChanged;
        if (latestNotifier is not null) latestNotifier.PropertyChanged += LatestChanged;
        Refresh();
    }
    private void LatestChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Original" or "Translation" or "Translation2" or "Translation3" or null or "")
        {
            if (Dispatcher.CheckAccess()) Refresh();
            else Dispatcher.BeginInvoke(new Action(Refresh));
        }
    }
    private async void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(new Action(Refresh)); return; }
        var token = ++revision;
        try
        {
            var slot = OverlayRomaji.IsJapanese(config.ResolvePrimaryTargetLanguage("Mic")) ? "Translation" :
                OverlayRomaji.IsJapanese(config.ResolveSecondaryTargetLanguage("Mic")) ? "Translation2" :
                OverlayRomaji.IsJapanese(config.ResolveTertiaryTargetLanguage("Mic")) ? "Translation3" : "Translation";
            var text = Get(latest, slot);
            original.Text = Get(latest, "Original");
            translation.Text = string.IsNullOrWhiteSpace(text) ? "翻译中…" : text;
            romaji.Text = string.IsNullOrWhiteSpace(text) ? "等待译文…" : "生成注音…";
            contentScroll.ScrollToTop();
            var targetJapanese = new[] { config.ResolvePrimaryTargetLanguage("Mic"), config.ResolveSecondaryTargetLanguage("Mic"), config.ResolveTertiaryTargetLanguage("Mic") }.Any(OverlayRomaji.IsJapanese);
            var value = targetJapanese && !string.IsNullOrWhiteSpace(text) ? await OverlayRomaji.ReadingAsync(text) : null;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!closed && token == revision) romaji.Text = value ?? (string.IsNullOrWhiteSpace(text) ? "等待译文…" : targetJapanese ? "暂无可用注音" : "将麦克风目标语言设为日语以显示罗马音");
            });
        }
        catch
        {
            if (!Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() => { if (!closed && token == revision) romaji.Text = "暂无可用注音"; });
        }
    }

    private void Position()
    {
        if (movedByUser || closed || !IsVisible || owner.WindowState == WindowState.Minimized) return;
        var anchor = new WindowInteropHelper(owner).Handle;
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(anchor, out var bounds) || !GetWindowRect(handle, out var popup) ||
            !GetMonitorInfo(MonitorFromWindow(anchor, 2), ref monitor)) return;
        var area = monitor.Work;
        var scale = VisualTreeHelper.GetDpi(owner);
        Width = Math.Min(Math.Clamp(owner.ActualWidth, 320, 580), (area.Right - area.Left) / scale.DpiScaleX);
        MaxHeight = Math.Min(340, (area.Bottom - area.Top) / scale.DpiScaleY);
        var gap = (int)Math.Round(6 * scale.DpiScaleY);
        var height = popup.Bottom - popup.Top;
        var x = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - (popup.Right - popup.Left)));
        var y = bounds.Top - gap - height;
        var width = popup.Right - popup.Left;
        if (y < area.Top)
        {
            if (bounds.Bottom + gap + height <= area.Bottom) y = bounds.Bottom + gap;
            else
            {
                y = bounds.Top;
                if (bounds.Right + gap + width <= area.Right) x = bounds.Right + gap;
                else if (bounds.Left - gap - width >= area.Left) x = bounds.Left - gap - width;
            }
        }
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
        if (popup.Left != x || popup.Top != y)
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    private void OwnerPositionChanged(object? sender, EventArgs e) => Position();
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => Position();
    private void OwnerStateChanged(object? sender, EventArgs e) => SyncVisibility();
    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibility();
    private void SyncVisibility()
    {
        if (closed || Owner is null || !owner.IsLoaded) return;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized) Hide();
        else { if (!IsVisible) Show(); Position(); }
    }
    private void OwnerClosed(object? sender, EventArgs e)
    {
        closed = true;
        revision++;
        if (viewModel is not null) viewModel.PropertyChanged -= ViewModelChanged;
        if (collection is not null) collection.CollectionChanged -= CollectionChanged;
        if (latestNotifier is not null) latestNotifier.PropertyChanged -= LatestChanged;
        if (IsLoaded) Close();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!closed) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
