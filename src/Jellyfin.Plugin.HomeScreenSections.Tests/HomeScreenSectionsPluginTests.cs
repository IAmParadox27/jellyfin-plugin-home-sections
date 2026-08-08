using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

[Collection("Plugin Instance")]
public class HomeScreenSectionsPluginTests
{
    private readonly PluginFixture _fixture;

    public HomeScreenSectionsPluginTests(PluginFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Instance_is_set_after_construction()
    {
        Assert.Same(_fixture.Plugin, HomeScreenSectionsPlugin.Instance);
    }

    [Fact]
    public void Plugin_exposes_stable_id_and_name()
    {
        Assert.Equal(Guid.Parse("b8298e01-2697-407a-b44d-aa8dc795e850"), _fixture.Plugin.Id);
        Assert.Equal("Home Screen Sections", _fixture.Plugin.Name);
    }

    [Fact]
    public void Constructor_registers_plugin_pages_configuration_file()
    {
        string pluginPagesConfig = Path.Combine(
            _fixture.Paths.PluginConfigurationsPath,
            "Jellyfin.Plugin.PluginPages",
            "config.json");

        Assert.True(File.Exists(pluginPagesConfig), $"Expected PluginPages config at {pluginPagesConfig}");
        string contents = File.ReadAllText(pluginPagesConfig);
        Assert.Contains("Jellyfin.Plugin.HomeScreenSections", contents, StringComparison.Ordinal);
        Assert.Contains("ModularHomeViews/settings", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPages_yields_admin_configuration_page()
    {
        List<PluginPageInfo> pages = [.. _fixture.Plugin.GetPages()];

        PluginPageInfo page = Assert.Single(pages);
        Assert.Equal("Home Screen Sections", page.Name);
        Assert.EndsWith(".Configuration.config.html", page.EmbeddedResourcePath, StringComparison.Ordinal);
        Assert.True(page.EnableInMainMenu);
        Assert.NotNull(_fixture.Plugin.GetType().Assembly.GetManifestResourceStream(page.EmbeddedResourcePath));
    }

    [Fact]
    public void GetViews_yields_settings_view_with_existing_resource()
    {
        List<PluginPageInfo> views = [.. _fixture.Plugin.GetViews()];

        PluginPageInfo view = Assert.Single(views);
        Assert.Equal("settings", view.Name);
        Assert.EndsWith(".Config.settings.html", view.EmbeddedResourcePath, StringComparison.Ordinal);
        Assert.NotNull(_fixture.Plugin.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath));
    }

    [Fact]
    public void GetCurrentPluginVersion_matches_assembly_version()
    {
        string expected = typeof(HomeScreenSectionsPlugin).Assembly.GetName().Version!.ToString();
        Assert.Equal(expected, _fixture.Plugin.GetCurrentPluginVersion());
    }

    [Fact]
    public void UpdateConfiguration_increments_cache_bust_when_developer_mode_turns_on()
    {
        HomeScreenSectionsPlugin plugin = _fixture.Plugin;
        PluginConfiguration original = plugin.Configuration;
        int counter = plugin.Configuration.CacheBustCounter;
        try
        {
            plugin.UpdateConfiguration(new PluginConfiguration { DeveloperMode = true });

            Assert.Equal(counter + 1, plugin.Configuration.CacheBustCounter);
        }
        finally
        {
            // UpdateConfiguration replaces the shared fixture's Configuration object; restore it
            // so DeveloperMode/other fields do not leak into later tests in the collection.
            plugin.UpdateConfiguration(original);
        }
    }

    [Fact]
    public void UpdateConfiguration_preserves_counter_when_developer_mode_stays_on()
    {
        HomeScreenSectionsPlugin plugin = _fixture.Plugin;
        PluginConfiguration original = plugin.Configuration;
        try
        {
            plugin.UpdateConfiguration(new PluginConfiguration { DeveloperMode = true });
            int counter = plugin.Configuration.CacheBustCounter;

            plugin.UpdateConfiguration(new PluginConfiguration { DeveloperMode = true });

            Assert.Equal(counter, plugin.Configuration.CacheBustCounter);
        }
        finally
        {
            plugin.UpdateConfiguration(original);
        }
    }

    [Fact]
    public void UpdateConfiguration_preserves_counter_when_developer_mode_turns_off()
    {
        HomeScreenSectionsPlugin plugin = _fixture.Plugin;
        PluginConfiguration original = plugin.Configuration;
        try
        {
            plugin.UpdateConfiguration(new PluginConfiguration { DeveloperMode = true });
            int counter = plugin.Configuration.CacheBustCounter;

            plugin.UpdateConfiguration(new PluginConfiguration { DeveloperMode = false });

            Assert.Equal(counter, plugin.Configuration.CacheBustCounter);
        }
        finally
        {
            plugin.UpdateConfiguration(original);
        }
    }

    [Fact]
    public void BustCache_increments_counter_and_clears_user_sections_cache()
    {
        HomeScreenSectionsPlugin plugin = _fixture.Plugin;
        int counter = plugin.Configuration.CacheBustCounter;
        _fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };
        try
        {
            plugin.BustCache();

            Assert.Equal(counter + 1, plugin.Configuration.CacheBustCounter);
            Assert.Empty(_fixture.SectionsCache.Cache);
        }
        finally
        {
            plugin.Configuration.CacheBustCounter = counter;
        }
    }

    [Fact]
    public void UpdateConfiguration_clears_user_sections_cache()
    {
        HomeScreenSectionsPlugin plugin = _fixture.Plugin;
        PluginConfiguration original = plugin.Configuration;
        _fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };
        try
        {
            plugin.UpdateConfiguration(new PluginConfiguration());

            Assert.Empty(_fixture.SectionsCache.Cache);
        }
        finally
        {
            plugin.UpdateConfiguration(original);
        }
    }

    [Fact]
    public void ClearUserSectionsDataCache_empties_the_cache()
    {
        _fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };

        _fixture.Plugin.ClearUserSectionsDataCache();

        Assert.Empty(_fixture.SectionsCache.Cache);
    }

    [Fact]
    public void Embedded_localization_resources_are_shipped()
    {
        Assembly pluginAssembly = typeof(HomeScreenSectionsPlugin).Assembly;

        string[] localizationResources = pluginAssembly.GetManifestResourceNames()
            .Where(name => name.Contains("_Localization.", StringComparison.Ordinal))
            .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(localizationResources);
        Assert.Contains(
            localizationResources,
            name => name.EndsWith("_Localization.en.json", StringComparison.Ordinal));
    }
}
