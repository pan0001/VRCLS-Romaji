using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VRCLS.UI.Views;

internal static class SettingsChecks
{
    public static async Task Run(Window owner, string directory, Func<Task> verifyOriginalSpeech)
    {
        // No SettingsViewModel services or account are needed to render the real settings dialog.
        async Task<(SettingsWindow, ToggleButton)> Open()
        {
            var window = new SettingsWindow(null!)
            {
                Owner = owner, ShowInTaskbar = false, ShowActivated = false,
                DataContext = new TestSettingsModel()
            };
            window.Show();
            for (int i = 0; i < 100; i++)
            {
                var toggle = Find(window).SingleOrDefault();
                if (toggle?.IsVisible == true) return (window, toggle);
                await Task.Delay(30);
            }
            throw new Exception("Microphone original-only toggle missing in recognition settings");
        }

        var marker = Path.Combine(directory, "mic-original-only.enabled");
        var (first, toggle) = await Open();
        if (toggle.IsChecked != false || File.Exists(marker)) throw new Exception("Original-only should default to off");
        Click(toggle);
        if (toggle.IsChecked != true || !File.Exists(marker)) throw new Exception("Original-only enable was not persisted");
        first.Close();
        var (second, restored) = await Open();
        if (restored.IsChecked != true) throw new Exception("Reopened dialog lost the preference");
        await verifyOriginalSpeech();
        Click(restored);
        if (restored.IsChecked != false || File.Exists(marker)) throw new Exception("Original-only disable was not persisted");
        second.Close();
        Console.WriteLine("PASS Official recognition settings show a single original-only toggle, and enable/reopen/disable persist");
    }

    private static IEnumerable<ToggleButton> Find(DependencyObject root)
    {
        if (root is ToggleButton { Name: "MicOriginalOnly" } toggle) yield return toggle;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Click(ToggleButton toggle)
    {
        var peer = new ToggleButtonAutomationPeer(toggle);
        ((IToggleProvider)peer).Toggle();
    }
}

public sealed class TestSettingsModel
{
    public int SelectedSettingsTabIndex { get; set; } = 1;
}
