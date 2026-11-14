# Multi-instance regression tests

The .NET tests require the .NET 10 SDK. They use mocked Jellyfin services and HTTP handlers; no running Jellyfin, Sonarr or Radarr server is required.

From the repository root:

```powershell
dotnet test src/Jellyfin.Plugin.HomeScreenSections.sln -p:JellyfinVersion=12.1.0
dotnet test src/Jellyfin.Plugin.HomeScreenSections.sln -p:JellyfinVersion=10.11.11
```

The browser tests exercise the actual configuration page in Chromium with mocked Jellyfin endpoints. They cover legacy configuration migration, two named instances per service, deleting the final instance, URL/API-key validation, labels containing HTML and reopening the page.

```powershell
Set-Location tests/ui
npm install
npx playwright install chromium
npm test
```

To use an existing compatible Chromium browser, set `HSS_BROWSER_EXECUTABLE` to its absolute executable path before running the UI tests.

The supplied project supports Jellyfin 10.11.x and 12.x. Its project file has no target framework configuration for Jellyfin 10.10.7.
