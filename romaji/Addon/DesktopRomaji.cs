using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VRCLS.Core.Models;

namespace VRCLS.Romaji;

/// <summary>Adds a separate presentation-only text line to the installed WPF template.</summary>
public static class DesktopRomaji
{
    private static readonly ConditionalWeakTable<Window, WindowHook> Windows = new();
    private static readonly ConditionalWeakTable<TextBlock, Annotation> Annotations = new();

    public static void Attach(Window window, AppConfig config)
    {
        Windows.GetValue(window, _ => new WindowHook(window, config));
    }

    private sealed class WindowHook
    {
        private readonly Window window;
        private readonly AppConfig config;
        private readonly HashSet<ListBox> lists = new();
        private bool queued;
        public WindowHook(Window window, AppConfig config)
        {
            this.window = window;
            this.config = config;
            window.Loaded += (_, _) => QueueScan();
            window.SizeChanged += (_, _) => QueueScan();
        }
        private void QueueScan()
        {
            if (queued || window.Dispatcher.HasShutdownStarted) return;
            queued = true;
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                queued = false;
                try
                {
                    var visuals = Descendants(window).ToArray();
                    foreach (var list in visuals.OfType<ListBox>())
                    {
                        if (!lists.Add(list)) continue;
                        list.ItemContainerGenerator.StatusChanged += (_, _) => QueueScan();
                        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => QueueScan()));
                    }
                    foreach (var text in visuals.OfType<TextBlock>())
                    {
                        var path = BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path;
                        if (path is not ("Translation" or "Translation2" or "Translation3") || text.Parent is not StackPanel panel) continue;
                        Annotations.GetValue(text, _ => new Annotation(text, panel, path, config)).Start();
                    }
                }
                catch { /* Original subtitles remain usable if a future template changes. */ }
            }));
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }

    private sealed class Annotation
    {
        private readonly TextBlock original;
        private readonly TextBlock reading;
        private readonly AppConfig config;
        private readonly string path;
        private readonly DependencyPropertyDescriptor descriptor =
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        private bool active;
        private int revision;

        public Annotation(TextBlock original, StackPanel panel, string path, AppConfig config)
        {
            this.original = original;
            this.path = path;
            this.config = config;
            reading = new TextBlock
            {
                FontSize = Math.Max(12, original.FontSize * .8),
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 2),
                Opacity = .9,
                IsHitTestVisible = false,
                Effect = original.Effect
            };
            reading.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = original });
            var visibility = new MultiBinding { Converter = new ReadingVisibility() };
            visibility.Bindings.Add(new Binding("Text") { Source = reading });
            visibility.Bindings.Add(new Binding("Visibility") { Source = original });
            reading.SetBinding(UIElement.VisibilityProperty, visibility);
            panel.Children.Insert(panel.Children.IndexOf(original), reading);
        }

        public void Start()
        {
            if (active) return;
            active = true;
            descriptor.AddValueChanged(original, Changed);
            original.DataContextChanged += ContextChanged;
            original.Unloaded += Unloaded;
            Refresh();
        }

        private void Changed(object? sender, EventArgs e) => Refresh();
        private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();
        private void Unloaded(object sender, RoutedEventArgs e)
        {
            active = false;
            revision++;
            descriptor.RemoveValueChanged(original, Changed);
            original.DataContextChanged -= ContextChanged;
            original.Unloaded -= Unloaded;
        }

        private async void Refresh()
        {
            var currentRevision = ++revision;
            reading.Text = "";
            try
            {
                var item = original.DataContext;
                if (!active || item is null) return;
                var source = item.GetType().GetProperty("Source")?.GetValue(item) as string;
                if (!string.Equals(source, "Mic", StringComparison.OrdinalIgnoreCase)) return;
                var target = path switch
                {
                    "Translation2" => config.ResolveSecondaryTargetLanguage("Mic"),
                    "Translation3" => config.ResolveTertiaryTargetLanguage("Mic"),
                    _ => config.ResolvePrimaryTargetLanguage("Mic")
                };
                if (!OverlayRomaji.IsJapanese(target)) return;
                var text = original.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                var value = await OverlayRomaji.ReadingAsync(text);
                // Virtualized rows can be reused while a reading is being computed.
                if (active && revision == currentRevision && ReferenceEquals(original.DataContext, item) && original.Text == text)
                    reading.Text = value ?? "";
            }
            catch { /* Preserve the translation when no reading is available. */ }
        }
    }

    private sealed class ReadingVisibility : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values[0] is string { Length: > 0 } && values[1] is Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
