using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace VRCLS.Romaji;

public static class MicOriginalSettings
{
    private const string ToggleName = "MicOriginalOnly";
    private static readonly string Marker = Path.Combine(
        AppContext.GetData("VRCLS.Romaji.SettingsDirectory") as string ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCLS-Romaji"),
        "mic-original-only.enabled");
    private static volatile bool enabled = File.Exists(Marker);
    private static bool registered;

    public static bool Enabled => enabled;

    internal static void Attach(Window mainWindow)
    {
        if (registered) return;
        var tabType = mainWindow.GetType().Assembly.GetType("VRCLS.UI.Views.SettingsTabs.RecognitionTab");
        if (tabType is null) return;
        EventManager.RegisterClassHandler(tabType, FrameworkElement.LoadedEvent, new RoutedEventHandler(TabLoaded));
        registered = true;
    }

    private static void TabLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not UserControl tab || !ReferenceEquals(sender, args.OriginalSource)) return;
        var devices = FindMicDevices(tab);
        if (devices?.Parent is not StackPanel panel) return;
        var existing = panel.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<ToggleButton>())
            .FirstOrDefault(t => t.Name == ToggleName);
        if (existing is not null)
        {
            existing.IsChecked = Enabled;
            return;
        }

        const string title = "只发送我说的原文";
        var toggle = new ToggleButton
        {
            Name = ToggleName,
            IsChecked = Enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(toggle, title);
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "SettingsToggleButtonStyle");
        toggle.Checked += Save;
        toggle.Unchecked += Save;
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "SettingsRowTitleTextStyle");
        var description = new TextBlock
        {
            Text = "跳过麦克风翻译，直接把识别出的原话发到 VRChat 聊天框。",
            TextWrapping = TextWrapping.Wrap
        };
        description.SetResourceReference(FrameworkElement.StyleProperty, "SettingsRowDescriptionTextStyle");
        var labels = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        labels.Children.Add(heading);
        labels.Children.Add(description);
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(labels);
        panel.Children.Insert(panel.Children.IndexOf(devices) + 1, row);
    }

    private static ComboBox? FindMicDevices(DependencyObject parent)
    {
        if (parent is ComboBox combo &&
            BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty)?.Path.Path == "MicDevices")
            return combo;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            if (FindMicDevices(child) is { } found) return found;
        return null;
    }

    private static void Save(object sender, RoutedEventArgs args)
    {
        var toggle = (ToggleButton)sender;
        bool value = toggle.IsChecked == true;
        if (value == Enabled) return;
        try
        {
            if (value)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
                using var marker = File.Open(Marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            }
            else if (Directory.Exists(Path.GetDirectoryName(Marker))) File.Delete(Marker);
            enabled = value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            toggle.IsChecked = Enabled;
            MessageBox.Show(Window.GetWindow(toggle), ex.Message, "无法保存原文发送设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
