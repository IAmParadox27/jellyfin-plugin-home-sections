using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Controllers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Controllers;

[Collection("Plugin Instance")]
public class ModularHomeViewsControllerTests
{
    private readonly Mock<IHomeScreenManager> _homeScreenManager = new();
    private readonly Mock<ITranslationManager> _translationManager = new();

    public ModularHomeViewsControllerTests(PluginFixture fixture)
    {
        _ = fixture;
    }

    private ModularHomeViewsController MakeController()
    {
        return new ModularHomeViewsController(
            NullLogger<ModularHomeViewsController>.Instance,
            _homeScreenManager.Object,
            _translationManager.Object);
    }

    [Fact]
    public void GetSectionTypes_returns_registered_sections_with_default_view_mode()
    {
        PluginDefinedSection section = new PluginDefinedSection("MySection", "My Section")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
        NullViewModeSection nullViewModeSection = new NullViewModeSection();
        _homeScreenManager
            .Setup(manager => manager.GetSectionTypes())
            .Returns(new IHomeScreenSection[] { section, nullViewModeSection });

        QueryResult<HomeScreenSectionInfo> result = MakeController().GetSectionTypes();

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(SectionViewMode.Landscape, item.ViewMode));
        _translationManager.Verify(
            manager => manager.Translate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TranslationMetadata?>()),
            Times.Never());
    }

    [Fact]
    public void GetSectionTypes_translates_display_text_when_language_given()
    {
        PluginDefinedSection section = new PluginDefinedSection("ContinueWatching", "Continue Watching")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
        _homeScreenManager
            .Setup(manager => manager.GetSectionTypes())
            .Returns(new IHomeScreenSection[] { section });
        _translationManager
            .Setup(manager => manager.Translate("ContinueWatching", "de", "Continue Watching", null))
            .Returns("Weiter schauen");

        QueryResult<HomeScreenSectionInfo> result = MakeController().GetSectionTypes(" de ");

        HomeScreenSectionInfo item = Assert.Single(result.Items);
        Assert.Equal("Weiter schauen", item.DisplayText);
    }

    [Fact]
    public void GetView_serves_embedded_settings_view()
    {
        ActionResult result = MakeController().GetView("settings");

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/html", fileResult.ContentType);
    }

    [Fact]
    public void GetView_unknown_view_returns_not_found()
    {
        ActionResult result = MakeController().GetView("does-not-exist");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetUserSettings_returns_manager_settings_when_present()
    {
        Guid userId = Guid.NewGuid();
        ModularHomeUserSettings stored = new ModularHomeUserSettings { UserId = userId };
        _homeScreenManager
            .Setup(manager => manager.GetUserSettings(userId))
            .Returns(stored);

        ActionResult<ModularHomeUserSettings> result = MakeController().GetUserSettings(userId);

        Assert.Same(stored, result.Value);
    }

    [Fact]
    public void GetUserSettings_builds_defaults_from_admin_section_settings()
    {
        Guid userId = Guid.NewGuid();
        _homeScreenManager
            .Setup(manager => manager.GetUserSettings(userId))
            .Returns((ModularHomeUserSettings?)null);

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "EnabledOverridable", Enabled = true, AllowUserOverride = true },
            new SectionSettings { SectionId = "EnabledLocked", Enabled = true, AllowUserOverride = false },
            new SectionSettings { SectionId = "DisabledOverridable", Enabled = false, AllowUserOverride = true }
        ];
        try
        {
            ActionResult<ModularHomeUserSettings> result = MakeController().GetUserSettings(userId);

            Assert.NotNull(result.Value);
            ModularHomeUserSettings settings = result.Value!;
            Assert.Equal(userId, settings.UserId);
            Assert.Equal(2, settings.EnabledSections.Count);
            Assert.Contains("EnabledOverridable", settings.EnabledSections, StringComparer.Ordinal);
            Assert.Contains("EnabledLocked", settings.EnabledSections, StringComparer.Ordinal);
            Assert.DoesNotContain("DisabledOverridable", settings.EnabledSections, StringComparer.Ordinal);
            Assert.Equal("EnabledLocked", Assert.Single(settings.LockedSections));
            Assert.Equal(2, settings.DefaultEnabledSections.Count);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void GetTranslations_returns_pack_from_manager()
    {
        Dictionary<string, string> pack = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AdminSave"] = "Gemmer"
        };
        _translationManager
            .Setup(manager => manager.GetTranslationPack("da"))
            .Returns(pack);

        ActionResult<IDictionary<string, string>> result = MakeController().GetTranslations(" da ");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(pack, ok.Value);
    }

    [Fact]
    public void GetTranslations_returns_empty_dictionary_when_no_pack_available()
    {
        _translationManager
            .Setup(manager => manager.GetTranslationPack(It.IsAny<string>()))
            .Returns((IDictionary<string, string>?)null);

        ActionResult<IDictionary<string, string>> result = MakeController().GetTranslations();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        IDictionary<string, string> value = Assert.IsAssignableFrom<IDictionary<string, string>>(ok.Value);
        Assert.Empty(value);
    }

    [Fact]
    public void UpdateSettings_forwards_to_manager_and_returns_ok()
    {
        ModularHomeUserSettings settings = new ModularHomeUserSettings { UserId = Guid.NewGuid() };

        ActionResult result = MakeController().UpdateSettings(settings);

        Assert.IsType<OkResult>(result);
        _homeScreenManager.Verify(manager => manager.UpdateUserSettings(settings.UserId, settings), Times.Once());
    }

    private sealed class NullViewModeSection : IHomeScreenSection
    {
        public string? Section => "NullViewMode";

        public string? DisplayText { get; set; } = "Null View Mode";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            return new QueryResult<BaseItemDto>();
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                ViewMode = null
            };
        }
    }
}
