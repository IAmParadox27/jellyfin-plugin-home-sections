using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// IApplicationPaths implementation rooted in a per-fixture temp directory,
/// so plugin code that touches the file system never escapes the test sandbox.
/// </summary>
public sealed class FakeApplicationPaths : IApplicationPaths
{
    public FakeApplicationPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);

        ProgramDataPath = CreateSubDir("programdata");
        WebPath = CreateSubDir("web");
        ProgramSystemPath = CreateSubDir("system");
        DataPath = CreateSubDir("data");
        ImageCachePath = CreateSubDir("imagecache");
        PluginsPath = CreateSubDir("plugins");
        PluginConfigurationsPath = CreateSubDir("config");
        LogDirectoryPath = CreateSubDir("logs");
        ConfigurationDirectoryPath = CreateSubDir("configuration");
        CachePath = CreateSubDir("cache");
        TempDirectory = CreateSubDir("temp");
        TrickplayPath = CreateSubDir("trickplay");
        BackupPath = CreateSubDir("backup");

        SystemConfigurationFilePath = Path.Combine(ConfigurationDirectoryPath, "system.xml");
        VirtualDataPath = "%AppDataPath%";
    }

    public string Root { get; }

    public string ProgramDataPath { get; }

    public string WebPath { get; }

    public string ProgramSystemPath { get; }

    public string DataPath { get; }

    public string ImageCachePath { get; }

    public string PluginsPath { get; }

    public string PluginConfigurationsPath { get; }

    public string LogDirectoryPath { get; }

    public string ConfigurationDirectoryPath { get; }

    public string SystemConfigurationFilePath { get; }

    public string CachePath { get; }

    public string TempDirectory { get; }

    public string VirtualDataPath { get; }

    public string TrickplayPath { get; }

    public string BackupPath { get; }

    public void MakeSanityCheckOrThrow()
    {
        // All paths are pre-created by the constructor; nothing to validate.
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        Directory.CreateDirectory(path);
        string markerPath = Path.Combine(path, markerName);
        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, string.Empty);
        }
    }

    private string CreateSubDir(string name)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
