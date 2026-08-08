namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

internal static class RepoPaths
{
    public static string Root
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            DirectoryInfo? current = new(dir);
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Jellyfin.Plugin.HomeScreenSections", "_Localization");
                if (Directory.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate repository root from test base directory '{dir}'.");
        }
    }

    public static string LocalizationDir =>
        Path.Combine(Root, "src", "Jellyfin.Plugin.HomeScreenSections", "_Localization");

    public static string ManifestPath =>
        Path.Combine(Root, "manifest.json");
}
