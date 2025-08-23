using System.Reflection;

namespace Jellyfin.Plugin.HomeScreenSections.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class JellyfinVersionAttribute : Attribute
    {
        public string Version { get; set; }
        
        public JellyfinVersionAttribute(string version)
        {
            Version = version;
        }

        public static string? GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetCustomAttribute<JellyfinVersionAttribute>()?.Version;
        }
    }
}