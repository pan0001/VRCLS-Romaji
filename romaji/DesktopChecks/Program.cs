using System.Windows.Markup;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCLS.Core.Models;
using VRCLS.UI.ViewModels;
using VRCLS.UI.Views;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var preferences = Directory.CreateTempSubdirectory("vrcls-settings-test-");
        AppContext.SetData("VRCLS.Romaji.SettingsDirectory", preferences.FullName);
        // Test host has a different entry assembly; resolve application-root asset URIs to VRCLS.
        var resourceAssembly = typeof(System.Windows.Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(f => f.Name.Contains("resourceAssembly", StringComparison.OrdinalIgnoreCase));
        resourceAssembly.SetValue(null, typeof(MainWindow).Assembly);
        var app = new TestApp();
        System.Reflection.Assembly.Load("MaterialDesignThemes.Wpf");
        System.Reflection.Assembly.Load("MaterialDesignColors");
        var xml = XDocument.Load(args[0]);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = xml.Root!.Element(wpf + "Application.Resources")!.Element(wpf + "ResourceDictionary")!;
        resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        resources.SetAttributeValue(XNamespace.Xmlns + "materialDesign", "http://materialdesigninxaml.net/winfx/xaml/themes");
        resources.SetAttributeValue(XNamespace.Xmlns + "Converters", "clr-namespace:VRCLS.UI.Converters;assembly=VRCLS");
        foreach (var element in resources.Descendants().Where(x => x.Name.NamespaceName == "clr-namespace:VRCLS.UI.Converters").ToArray())
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:VRCLS.UI.Converters;assembly=VRCLS");
        foreach (var source in resources.Descendants().Attributes("Source").Where(x => x.Value.StartsWith("Resources/")))
            source.Value = "pack://application:,,,/VRCLS;component/" + source.Value;
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        var config = new AppConfig { MicTargetTranslationLanguage = "ja", HasSeenQuickStartGuide = true, SteamVrAutoLaunch = false };
        var window = new MainWindow(config) { Width = 580, Height = 620, ShowActivated = false, ShowInTaskbar = false, Left = 120, Top = 250 };
        var vm = new TestViewModel();
        var mic = new TranscriptItem { Source = "Mic", Original = "你好。", Translation = "こんにちは。" };
        var desktop = new TranscriptItem { Source = "Desktop", Original = "さようなら。", Translation = "再见。" };
        vm.Transcripts.Add(mic);
        vm.Transcripts.Add(desktop);
        window.DataContext = vm;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var popup = app.Windows.OfType<Window>().Single(w => w.Owner == window);
                TextBlock Line(string name) => Texts(popup).Single(t => t.Name == name);
                var reading = Line("SelfRomaji");
                await Wait(() => popup.IsVisible && reading.Text == "konnichiwa.");
                Check(!Texts(window).Any(t => t.Text == reading.Text), "Official list has no inserted pronunciation row");
                var ja = Texts(window).Single(t => t.Text == mic.Translation);
                var source = Texts(window).Single(t => t.Text == mic.Original);
                var panel = (StackPanel)ja.Parent;
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(scale, scale);
                    window.UpdateLayout();
                    Check(source.TranslatePoint(new Point(), panel).Y >= ja.TranslatePoint(new Point(), panel).Y + ja.ActualHeight - 1,
                        $"Main list has distinct translation and original lines at {scale:P0} layout scale");
                }
                ((FrameworkElement)window.Content).LayoutTransform = Transform.Identity;
                mic.Original = "初次见面，请多关照。";
                mic.Translation = "初めまして、よろしくお願いします。";
                await Wait(() => reading.Text.Contains("hajimemashite"));
                vm.Transcripts.Insert(0, new TranscriptItem { Source = "Desktop", Translation = "收到。" });
                vm.Transcripts.Remove(mic);
                await Task.Delay(80);
                Check(Line("SelfTranslation").Text == mic.Translation && Line("SelfOriginal").Text == mic.Original,
                    "Desktop speech and history trimming preserve the microphone sentence");
                popup.UpdateLayout();
                if (args.Length == 2)
                {
                    var content = (FrameworkElement)popup.Content;
                    var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(args[1]);
                    encoder.Save(file);
                }
                window.Width = 340;
                var next = new TranscriptItem { Source = "Mic", Original = "测试长句。",
                    Translation = string.Concat(Enumerable.Repeat("今日はいい天気ですね。", 12)) };
                vm.Transcripts.Insert(0, next);
                await Wait(() => reading.Text.Contains("tenki"));
                popup.UpdateLayout();
                var scroll = (ScrollViewer)System.Windows.LogicalTreeHelper.GetParent(reading.Parent);
                Check(reading.ActualHeight > reading.FontSize * 2 && scroll.ScrollableHeight > 0, "Long reading wraps and can scroll");
                Check(Line("SelfTranslation").TranslatePoint(new Point(), reading).Y >= reading.ActualHeight,
                    "Wrapped reading is above its translation");
                window.WindowState = WindowState.Minimized;
                await Wait(() => !popup.IsVisible);
                window.WindowState = WindowState.Normal;
                await Wait(() => popup.IsVisible);
                Check(reading.Text.Contains("tenki"), "Restoring main window restores pinned speech");
                await SettingsChecks.Run(window, preferences.FullName, async () =>
                {
                    var direct = new TranscriptItem { Source = "Mic", Original = "等我一下。" };
                    vm.Transcripts.Insert(0, direct);
                    await Wait(() => Line("SelfOriginal").Text == direct.Original);
                    Check(reading.Visibility == Visibility.Collapsed && Line("SelfTranslation").Visibility == Visibility.Collapsed,
                        "Original-only speech shows the original without a pending translation");
                });
                preferences.Delete();
                Environment.Exit(0); // Do not trigger the program's config-saving OnClosing/OnExit handlers.
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                foreach (var text in Texts(window).Take(20))
                    Console.WriteLine($"TEXT [{text.Text}] parent={text.Parent?.GetType().Name} context={text.DataContext?.GetType().Name} binding={System.Windows.Data.BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path}");
                Environment.Exit(1);
            }
        };
        app.Run(window);
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Texts(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); Console.WriteLine("PASS " + label); }
    private static async Task Wait(Func<bool> ready)
    {
        for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Desktop annotation did not update");
    }
}

internal class TestApp : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e) { } // No login, microphone, OSC, or network startup.
    protected override void OnExit(ExitEventArgs e) { }
}

public class TestViewModel : INotifyPropertyChanged
{
    public ObservableCollection<TranscriptItem> Transcripts { get; } = new();
    private bool show = true;
    public bool ShowTranslation1 { get => show; set { show = value; PropertyChanged?.Invoke(this, new(nameof(ShowTranslation1))); } }
    public bool ShowTranslation2 => true;
    public bool ShowTranslation3 => true;
    public bool ShowOriginal => true;
    public bool HasSeenQuickStartGuide => true;
    public bool IsQuickStartGuideVisible => false;
    public string HomeAlertMessage => "";
    public string RateLimitNoticeMessage => "";
    public string ConnectionStateText => "已连接";
    public bool IsMicListening => true;
    public bool IsDesktopListening => true;
    public event PropertyChangedEventHandler? PropertyChanged;
}
