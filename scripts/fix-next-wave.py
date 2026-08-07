"""Apply exact-string fixes for next-wave analyzer rules. No identifier rewriting."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(r"C:\projects\jellyfin-stuff\jellyfin-plugin-home-sections\src\Jellyfin.Plugin.HomeScreenSections")

# (relative path, old, new) — exact match replacements
REPLACEMENTS: list[tuple[str, str, str]] = [
    # --- Common SectionId == Section (MA0006) ---
    (
        "HomeScreen/Sections/ContinueWatchingNextUpSection.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/GenreSection.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/BecauseYouWatchedSection.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/LatestSectionBase.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/RecentlyAddedSectionBase.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/MyRequestsSection.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/Latest/LatestShowsSection.cs",
        "x => x.SectionId == Section",
        "x => string.Equals(x.SectionId, Section, StringComparison.Ordinal)",
    ),
    # jellyfinUsername == user.Username
    (
        "HomeScreen/Sections/DiscoverSection.cs",
        'x.Value<string>("jellyfinUsername") == user.Username',
        'string.Equals(x.Value<string>("jellyfinUsername"), user.Username, StringComparison.Ordinal)',
    ),
    (
        "Controllers/HomeScreenController.cs",
        'x.Value<string>("jellyfinUsername") == user.Username',
        'string.Equals(x.Value<string>("jellyfinUsername"), user.Username, StringComparison.Ordinal)',
    ),
    (
        "HomeScreen/Sections/MyRequestsSection.cs",
        'x.Value<string>("jellyfinUsername") == user.Username',
        'string.Equals(x.Value<string>("jellyfinUsername"), user.Username, StringComparison.Ordinal)',
    ),
    (
        "Controllers/HomeScreenController.cs",
        'if (payload.MediaType == "tv")',
        'if (string.Equals(payload.MediaType, "tv", StringComparison.Ordinal))',
    ),
    (
        "HomeScreen/Sections/NextUpSection.cs",
        'enableRewatching = enableRewatchingValue.FirstOrDefault() == "true";',
        'enableRewatching = string.Equals(enableRewatchingValue.FirstOrDefault(), "true", StringComparison.Ordinal);',
    ),
    (
        "HomeScreen/Sections/MyListSection.cs",
        'x => x.Name == "My List"',
        'x => string.Equals(x.Name, "My List", StringComparison.Ordinal)',
    ),
    (
        "HomeScreen/Sections/TopTenSection.cs",
        'x => x.Name == "Top Ten"',
        'x => string.Equals(x.Name, "Top Ten", StringComparison.Ordinal)',
    ),
    (
        "Helpers/PatchHelpers.cs",
        'x => x.Name == "StreamyfinController"',
        'x => string.Equals(x.Name, "StreamyfinController", StringComparison.Ordinal)',
    ),
    (
        "Helpers/PatchHelpers.cs",
        'info.Section == "MyMedia")',
        'string.Equals(info.Section, "MyMedia", StringComparison.Ordinal))',
    ),
    (
        "Helpers/PatchHelpers.cs",
        'x.FullName?.Contains("Jellyfin.Plugin.Streamyfin") ?? false',
        'x.FullName?.Contains("Jellyfin.Plugin.Streamyfin", StringComparison.Ordinal) ?? false',
    ),
    (
        "Helpers/PatchHelpers.cs",
        'info.Section?.StartsWith("Discover") ?? false',
        'info.Section?.StartsWith("Discover", StringComparison.Ordinal) ?? false',
    ),
    (
        "Helpers/PatchHelpers.cs",
        'info.Section?.StartsWith("Upcoming") ?? false',
        'info.Section?.StartsWith("Upcoming", StringComparison.Ordinal) ?? false',
    ),
    (
        "Helpers/TransformationPatches.cs",
        'JellyfinVersionAttribute.GetVersion()?.StartsWith("10.10.7") ?? false',
        'JellyfinVersionAttribute.GetVersion()?.StartsWith("10.10.7", StringComparison.Ordinal) ?? false',
    ),
    (
        "Helpers/TransformationPatches.cs",
        'JellyfinVersionAttribute.GetVersion()?.StartsWith("10.11") ?? false',
        'JellyfinVersionAttribute.GetVersion()?.StartsWith("10.11", StringComparison.Ordinal) ?? false',
    ),
    (
        "Helpers/TransformationPatches.cs",
        "Regex variableFind = new Regex(@\"var\\s+([a-zA-Z][^=]*)=\");",
        "Regex variableFind = new Regex(@\"var\\s+([a-zA-Z][^=]*)=\", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));",
    ),
    (
        "Services/StartupService.cs",
        'Regex r = new Regex(@"([^.]+)\.([^.]+)\.chunk.js");',
        'Regex r = new Regex(@"([^.]+)\.([^.]+)\.chunk.js", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));',
    ),
    (
        "Services/StartupService.cs",
        'if (File.ReadAllText(jsChunk).Contains(",loadSections:"))',
        'if ((await File.ReadAllTextAsync(jsChunk)).Contains(",loadSections:", StringComparison.Ordinal))',
    ),
    (
        "Services/StartupService.cs",
        "x.FullName?.Contains(\".FileTransformation\") ?? false);",
        "x.FullName?.Contains(\".FileTransformation\", StringComparison.Ordinal) ?? false);",
    ),
    (
        "Services/TranslationManager.cs",
        '.Where(x => x.EndsWith(".json") && x.Contains("_Localization.")).ToArray();',
        '.Where(x => x.EndsWith(".json", StringComparison.Ordinal) && x.Contains("_Localization.", StringComparison.Ordinal)).ToArray();',
    ),
    (
        "Services/TranslationManager.cs",
        "if (key != fullTextKey && translationPack.ContainsKey(fullTextKey))",
        "if (!string.Equals(key, fullTextKey, StringComparison.Ordinal) && translationPack.ContainsKey(fullTextKey))",
    ),
    (
        "ModuleInitializer.cs",
        "foreach (string resource in resources.Where(x => x.EndsWith(\".dll\")))",
        "foreach (string resource in resources.Where(x => x.EndsWith(\".dll\", StringComparison.Ordinal)))",
    ),
    (
        "ModuleInitializer.cs",
        "if (!assemblyLoadContext.Assemblies.Any(x => x.FullName == assemblyName.FullName))",
        "if (!assemblyLoadContext.Assemblies.Any(x => string.Equals(x.FullName, assemblyName.FullName, StringComparison.Ordinal)))",
    ),
    (
        "ModuleInitializer.cs",
        "loadedAssembly = assemblyLoadContext.Assemblies.First(x => x.FullName == assemblyName.FullName);",
        "loadedAssembly = assemblyLoadContext.Assemblies.First(x => string.Equals(x.FullName, assemblyName.FullName, StringComparison.Ordinal));",
    ),
    (
        "HomeScreenSectionsPlugin.cs",
        'x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace) as JObject;',
        'string.Equals(x.Value<string>("Id"), typeof(HomeScreenSectionsPlugin).Namespace, StringComparison.Ordinal)) as JObject;',
    ),
    (
        "HomeScreenSectionsPlugin.cs",
        'if (!config.Value<JArray>("pages")!.Any(x => x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace))',
        'if (!config.Value<JArray>("pages")!.Any(x => string.Equals(x.Value<string>("Id"), typeof(HomeScreenSectionsPlugin).Namespace, StringComparison.Ordinal)))',
    ),
    (
        "HomeScreenSectionsPlugin.cs",
        'Assembly? pluginPagesAssembly = AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages") ?? false);',
        'Assembly? pluginPagesAssembly = AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages", StringComparison.Ordinal) ?? false);',
    ),
    (
        "Controllers/ModularHomeViewsController.cs",
        "pageInfo => pageInfo?.Name == viewName",
        "pageInfo => string.Equals(pageInfo?.Name, viewName, StringComparison.Ordinal)",
    ),
    (
        "Services/DailyTranslationCacheService.cs",
        'x => x.Key == "GitBranch"',
        'x => string.Equals(x.Key, "GitBranch", StringComparison.Ordinal)',
    ),
    (
        "Services/DailyTranslationCacheService.cs",
        'x.Value<string>("path")?.StartsWith(c_locPath) ?? false',
        'x.Value<string>("path")?.StartsWith(c_locPath, StringComparison.Ordinal) ?? false',
    ),
    (
        "PluginInterface.cs",
        "x => x.FullName == payload.ResultsAssembly",
        "x => string.Equals(x.FullName, payload.ResultsAssembly, StringComparison.Ordinal)",
    ),
    (
        "PluginInterface.cs",
        "x => x.FullName == payload.ResultsClass",
        "x => string.Equals(x.FullName, payload.ResultsClass, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/LatestSectionBase.cs",
        "x => x.Id.ToString() == LibraryId",
        "x => string.Equals(x.Id.ToString(), LibraryId, StringComparison.Ordinal)",
    ),
    (
        "HomeScreen/Sections/RecentlyAddedSectionBase.cs",
        "x => x.Id.ToString() == LibraryId",
        "x => string.Equals(x.Id.ToString(), LibraryId, StringComparison.Ordinal)",
    ),
    (
        "Services/HomeScreenSectionService.cs",
        "x => x.Section == sectionSettings.SectionId",
        "x => string.Equals(x.Section, sectionSettings.SectionId, StringComparison.Ordinal)",
    ),
    (
        "Services/HomeScreenSectionService.cs",
        "y => y.SectionId == info.Section",
        "y => string.Equals(y.SectionId, info.Section, StringComparison.Ordinal)",
    ),
    # CA2201
    (
        "HomeScreen/Sections/GenreSection.cs",
        "throw new Exception();",
        'throw new InvalidOperationException("User not found for genre section.");',
    ),
    (
        "HomeScreen/HomeScreenManager.cs",
        "throw new Exception($\"Section type '{handler.Section}' has already been registered to type '{m_delegates[handler.Section].GetType().FullName}'.\");",
        "throw new InvalidOperationException($\"Section type '{handler.Section}' has already been registered to type '{m_delegates[handler.Section].GetType().FullName}'.\");",
    ),
    # CA1822 static
    (
        "HomeScreen/Sections/ContinueWatchingNextUpSection.cs",
        "private DateTime GetSortDate(BaseItemDto item, Dictionary<Guid, DateTime> seriesLastPlayed)",
        "private static DateTime GetSortDate(BaseItemDto item, Dictionary<Guid, DateTime> seriesLastPlayed)",
    ),
    (
        "HomeScreen/Sections/Upcoming/UpcomingMoviesSection.cs",
        "private DateTime GetEarliestReleaseDate(RadarrCalendarDto item, PluginConfiguration config)",
        "private static DateTime GetEarliestReleaseDate(RadarrCalendarDto item, PluginConfiguration config)",
    ),
    # CA1859
    (
        "HomeScreen/Sections/Persons/PersonsSectionBase.cs",
        "IReadOnlyList<BaseItem> personItems = folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery()",
        "List<BaseItem> personItems = folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery()",
    ),
    # CA1860 RecentlyAddedShows
    (
        "HomeScreen/Sections/RecentlyAdded/RecentlyAddedShowsSection.cs",
        "dateCreated = (seasonEpisodes?.Any() ?? false) ? seasonEpisodes.Max(x => x.DateCreated) : null;",
        "dateCreated = (seasonEpisodes is { Count: > 0 }) ? seasonEpisodes.Max(x => x.DateCreated) : null;",
    ),
]


def main() -> None:
    applied = 0
    missing = 0
    for rel, old, new in REPLACEMENTS:
        path = ROOT / rel
        if not path.exists():
            print(f"MISSING FILE: {rel}")
            missing += 1
            continue
        text = path.read_text(encoding="utf-8-sig")
        if old not in text:
            print(f"NOT FOUND: {rel}: {old[:80]!r}")
            missing += 1
            continue
        count = text.count(old)
        text = text.replace(old, new)
        path.write_text(text, encoding="utf-8", newline="\n")
        print(f"OK ({count}x): {rel}: {old[:60]!r}")
        applied += 1
    print(f"\napplied={applied} missing={missing}")


if __name__ == "__main__":
    main()
