# Install this fork through the Jellyfin plugin catalog

This repository publishes the multiple-instance fork for Jellyfin **10.11.11**.

Repository name: **Home Screen Sections - agzina0sicim**

Repository URL:

```text
https://raw.githubusercontent.com/agzina0sicim/jellyfin-plugin-home-sections/codex/plugin-repository/manifest.json
```

1. In the Jellyfin dashboard, open Plugins, then Repositories.
2. Add the repository name and URL above and save.
3. Open the plugin catalog and select Home Screen Sections.
4. Install version **3.0.3.1**, then restart the Jellyfin Docker container.
5. Refresh the web client. Open the Home Screen Sections configuration and add the Sonarr and Radarr instances under *arr API Settings.

Keep compatible versions of **File Transformation** and **Plugin Pages** installed. The upstream plugin repository can remain enabled to supply these dependencies.

This fork keeps the original plugin identifier so that existing Home Screen Sections configuration can be reused. Version 3.0.3.1 includes the multi-instance changes from commit 986c1cc5b438cb6473e328707d4f759e19606f11. It is an unofficial fork, not an upstream release.

The GitHub release asset contains the compiled plugin, dependency metadata, symbols and logo. Jellyfin downloads and verifies the asset using the checksum recorded in manifest.json.

Future upstream versions have higher version numbers and may appear as updates. Select the desired fork version from this repository when testing the multi-instance feature.

AI assistance: Codex.
Contributor's coding experience without AI: Beginner.
