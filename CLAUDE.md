# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
# Build (JellyfinVersion selects TFM: 10.10.7→net8.0, 10.11.x→net9.0)
dotnet build src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj -c Release -p:JellyfinVersion=10.11.11
dotnet build src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj -c Release -p:JellyfinVersion=10.10.7

# Tests (all) — requires both TFMs installed
dotnet test src/Jellyfin.Plugin.HomeScreenSections.Tests/Jellyfin.Plugin.HomeScreenSections.Tests.csproj -c Release --verbosity normal

# Single test
dotnet test src/Jellyfin.Plugin.HomeScreenSections.Tests/ --filter "FullyQualifiedName~TestClassName.TestMethod"

# With coverage (CI uses 80% line gate)
dotnet test src/Jellyfin.Plugin.HomeScreenSections.Tests/Jellyfin.Plugin.HomeScreenSections.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Release zips (stamps version, builds per-JF zip)
./build-release.sh 2.5.18.0 10.10.7,10.11.5,10.11.11 ./dist

# Regenerate catalogue manifest from GitHub releases
./scripts/generate-manifest.sh Robert-Hansen/jellyfin-plugin-home-sections manifest.json
```

Analyzers are enforced: `src/Directory.Build.props` enables `Meziantou.Analyzer` + `Microsoft.CodeAnalysis.NetAnalyzers` with `TreatWarningsAsErrors=true`. Build must be warning-free.

## Architecture

Jellyfin server plugin (C#) that replaces the vanilla home screen with configurable "modular" sections. Requires `File Transformation` + `Plugin Pages` companion plugins.

- **Entry**: `HomeScreenSectionsPlugin` (`HomeScreenSectionsPlugin.cs`) — `BasePlugin<PluginConfiguration>`, registers `PluginPages` pages and calls `IHomeScreenManager.RegisterBuiltInResultsDelegates()`.
- **DI**: `PluginServiceRegistrator` + `ModuleInitializer` wire services; `HomeScreenSectionsPlugin.Instance.ServiceProvider` is the service locator used by `PluginInterface`.
- **Section registry**: `HomeScreen/HomeScreenManager.cs` — `Dictionary<string, IHomeScreenSection>` keyed by section id. `RegisterBuiltInResultsDelegates()` table-drives ~36 section types; `InvokeResultsDelegate(key, payload, query)` dispatches. Per-user state (`userFeatureEnabled.json`, `ModularHomeSettings.json`) lives under `PluginConfigurationsPath/Jellyfin.Plugin.HomeScreenSections/`.
- **Sections**: `HomeScreen/Sections/` — each implements `IHomeScreenSection` (`Section` string + `GetResults(payload, query) → QueryResult<BaseItemDto>`). Subfolders: `Extra/` (10 fork-added: Favorites, Surprise Me/RandomUnwatched, Trending/MostPlayed, RecentlyPlayed, Kids, ComingSoon, Decade, Studio, Playlists, UnwatchedCollections), `Latest/`, `RecentlyAdded/`, `Upcoming/`, `Persons/`. Title routes validated to avoid dead chevrons.
- **External section API**: `PluginInterface.RegisterSection(JObject)` — reflection-based; third-party plugins invoke via `AssemblyLoadContext.All` to register `PluginDefinedSection` (assembly/class/method or HTTP endpoint).
- **Controllers**: `Controllers/HomeScreenController.cs` (modular home data, diagnostics `GET HomeScreen/Diagnostics`, cache bust) and `ModularHomeViewsController.cs`.
- **Config**: `Configuration/PluginConfiguration.cs` — `Enabled`, `SectionSettings[]` (per-section `Enabled/OrderIndex/ViewMode/LowerLimit/UpperLimit/HideWatchedItems`), `ArrConfig` (Sonarr/Radarr/Lidarr/Readarr), Jellyseerr/LibreTranslate, `CacheBustCounter`. Admin UI in `Configuration/config.html` + `Config/settings.html`.
- **Services**: `Services/` — `HomeScreenSectionService`, `ImageCacheService`/`ImageCacheCleanupTask`, `ArrApiService`, `TranslationManager`/`DailyTranslationCacheService`.
- **Client injection**: `Inject/HomeScreenSections.{js,css}` + `Controllers/loadSections.js` embedded resources patched in via `File Transformation` (Harmony `Lib.Harmony`).
- **Version shims**: `JellyfinVersionSpecific/{10.10.7,10.11.0,10.11}/` — conditionally compiled per `JellyfinVersion` property (`<Compile Remove>` in csproj).
- **Localization**: `src/Jellyfin.Plugin.HomeScreenSections/_Localization/*.json` (en, de, pl, da with CI key-parity check).
- **CI**: `.github/workflows/ci.yml` (build both TFMs + test + 80% line coverage gate), `build-release.yml` (matrix build → zips → GitHub Release → manifest commit).

## Conventions

- Follow standard C# naming conventions (Microsoft / .NET guidelines):
  - Private instance fields: `_camelCase`
  - Private static fields: `s_camelCase` (or `_camelCase` if preferred for consistency)
  - Constants (`const`) and `static readonly`: `PascalCase`
  - Public / protected / internal members, types, methods, properties: `PascalCase`
  - Local variables and parameters: `camelCase`
  - Do not use Hungarian notation or prefixes such as `m_`, `c_`, etc.
- Braces on new lines (Allman style) even for single-line blocks.
- Prefer explicit types over `var` unless the type is immediately obvious from the right-hand side or needed for namespace disambiguation.
- No whitespace-only diffs.
- Jellyfin version pinned via `JellyfinVersion` MSBuild property; `Directory.Build.props` GitBranch/JellyfinVersion assembly metadata targets.
