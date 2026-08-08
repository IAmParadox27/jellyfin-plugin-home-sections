using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Jellyfin.Plugin.HomeScreenSections;

public class ModuleInitializer
{
    private static readonly Dictionary<string, Assembly> s_dynamicAssemblies = new(StringComparer.Ordinal);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        Assembly assembly = typeof(HomeScreenSectionsPlugin).Assembly;
        AssemblyLoadContext assemblyLoadContext = new("Jellyfin.Plugin.HomeScreenSections");

        foreach (string resource in assembly.GetManifestResourceNames().Where(x => x.EndsWith(".dll", StringComparison.Ordinal)))
        {
            using Stream? assemblyStream = assembly.GetManifestResourceStream(resource);
            if (assemblyStream == null)
            {
                continue;
            }

            Assembly loadedAssembly = assemblyLoadContext.LoadFromStream(assemblyStream);
            s_dynamicAssemblies.TryAdd(loadedAssembly.FullName!, loadedAssembly);
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            if (s_dynamicAssemblies.TryGetValue(args.Name!, out Assembly? resolved))
            {
                return resolved;
            }

            return null;
        };
    }
}
