using System.IO;
namespace VRCLS.Romaji;
public static class ForkBridge
{
    public static string LauncherPath() => Environment.GetEnvironmentVariable("VRCLS_ROMAJI_LAUNCHER") ?? Environment.ProcessPath!;
    public static string ManifestDirectory() => Path.GetDirectoryName(LauncherPath()) ?? AppContext.BaseDirectory;
}
