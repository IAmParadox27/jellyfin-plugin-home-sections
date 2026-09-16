using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Jellyfin.Plugin.HomeScreenSections.Helpers;

namespace Jellyfin.Plugin.HomeScreenSections;

public class ModuleInitializer
{
    private static Dictionary<string, Assembly> s_dynamicAssemblies = new Dictionary<string, Assembly>();

    [ModuleInitializer]
    public static void Initialize()
    {
        Assembly assembly = typeof(HomeScreenSectionsPlugin).Assembly;
        AssemblyLoadContext assemblyLoadContext = new AssemblyLoadContext("Jellyfin.Plugin.HomeScreenSections");
        string[] resources = assembly.GetManifestResourceNames();
            
        foreach (string resource in resources.Where(x => x.EndsWith(".dll")))
        {
            using Stream? assemblyStream = assembly.GetManifestResourceStream(resource);
            using MemoryStream memoryStream = new MemoryStream();
            assemblyStream!.CopyTo(memoryStream);
            assemblyStream.Position = 0;
                
            string tmpDllLocation = $"{Path.GetTempFileName()}.dll";
                
            File.WriteAllBytes(tmpDllLocation, memoryStream.ToArray());
                
            AssemblyName assemblyName = AssemblyName.GetAssemblyName(tmpDllLocation);
            File.Delete(tmpDllLocation);
                
            Assembly loadedAssembly;
            if (!assemblyLoadContext.Assemblies.Any(x => x.FullName == assemblyName.FullName))
            {
                loadedAssembly = assemblyLoadContext.LoadFromStream(assemblyStream);
            }
            else
            {
                loadedAssembly = assemblyLoadContext.Assemblies.First(x => x.FullName == assemblyName.FullName);
            }
                
            s_dynamicAssemblies.Add(loadedAssembly.FullName!, loadedAssembly);
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            if (s_dynamicAssemblies.ContainsKey(args.Name!))
            {
                return s_dynamicAssemblies[args.Name!];
            }
                
            return null;
        };
    }
}