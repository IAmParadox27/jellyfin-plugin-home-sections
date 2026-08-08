using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

[Collection("Plugin Instance")]
public class HomeScreenSectionsPluginTests
{
    private readonly PluginFixture m_fixture;

    public HomeScreenSectionsPluginTests(PluginFixture fixture)
    {
        m_fixture = fixture;
    }

    [Fact]
    public void Instance_is_set_after_construction()
    {
        Assert.Same(m_fixture.Plugin, HomeScreenSectionsPlugin.Instance);
    }

    [Fact]
    public void Plugin_exposes_stable_id_and_name()
    {
        Assert.Equal(Guid.Parse("b8298e01-2697-407a-b44d-aa8dc795e850"), m_fixture.Plugin.Id);
        Assert.Equal("Home Screen Sections", m_fixture.Plugin.Name);
    }

    [Fact]
    public void Constructor_registers_plugin_pages_configuration_file()
    {
        string pluginPagesConfig = Path.Combine(
            m_fixture.Paths.PluginConfigurationsPath,
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
        List<PluginPageInfo> pages = [.. m_fixture.Plugin.GetPages()];

        PluginPageInfo page = Assert.Single(pages);
        Assert.Equal("Home Screen Sections", page.Name);
        Assert.EndsWith(".Configuration.config.html", page.EmbeddedResourcePath, StringComparison.Ordinal);
        Assert.True(page.EnableInMainMenu);
        Assert.NotNull(m_fixture.Plugin.GetType().Assembly.GetManifestResourceStream(page.EmbeddedResourcePath));
    }

    [Fact]
    public void GetViews_yields_settings_view_with_existing_resource()
    {
        List<PluginPageInfo> views = [.. m_fixture.Plugin.GetViews()];

        PluginPageInfo view = Assert.Single(views);
        Assert.Equal("settings", view.Name);
        Assert.EndsWith(".Config.settings.html", view.EmbeddedResourcePath, StringComparison.Ordinal);
        Assert.NotNull(m_fixture.Plugin.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath));
    }

    [Fact]
    public void GetCurrentPluginVersion_matches_assembly_version()
    {
        string expected = typeof(HomeScreenSectionsPlugin).Assembly.GetName().Version!.ToString();
        Assert.Equal(expected, m_fixture.Plugin.GetCurrentPluginVersion());
    }

    [Fact]
    public void UpdateConfiguration_increments_cache_bust_when_developer_mode_turns_on()
    {
        HomeScreenSectionsPlugin plugin = m_fixture.Plugin;
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
        HomeScreenSectionsPlugin plugin = m_fixture.Plugin;
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
        HomeScreenSectionsPlugin plugin = m_fixture.Plugin;
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
        HomeScreenSectionsPlugin plugin = m_fixture.Plugin;
        int counter = plugin.Configuration.CacheBustCounter;
        m_fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };
        try
        {
            plugin.BustCache();

            Assert.Equal(counter + 1, plugin.Configuration.CacheBustCounter);
            Assert.Empty(m_fixture.SectionsCache.Cache);
        }
        finally
        {
            plugin.Configuration.CacheBustCounter = counter;
        }
    }

    [Fact]
    public void UpdateConfiguration_clears_user_sections_cache()
    {
        HomeScreenSectionsPlugin plugin = m_fixture.Plugin;
        PluginConfiguration original = plugin.Configuration;
        m_fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };
        try
        {
            plugin.UpdateConfiguration(new PluginConfiguration());

            Assert.Empty(m_fixture.SectionsCache.Cache);
        }
        finally
        {
            plugin.UpdateConfiguration(original);
        }
    }

    [Fact]
    public void ClearUserSectionsDataCache_empties_the_cache()
    {
        m_fixture.SectionsCache.Cache[Guid.NewGuid()] = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 1
        };

        m_fixture.Plugin.ClearUserSectionsDataCache();

        Assert.Empty(m_fixture.SectionsCache.Cache);
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
