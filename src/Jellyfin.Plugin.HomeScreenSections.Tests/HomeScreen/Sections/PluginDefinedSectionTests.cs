using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

public class PluginDefinedSectionTests
{
    [Fact]
    public void Constructor_sets_properties_and_limit_of_one()
    {
        object payloadMarker = new object();
        PluginDefinedSection section = MakeSection(originalPayload: payloadMarker);

        Assert.Equal("test-uuid", section.Section);
        Assert.Equal("Test Section", section.DisplayText);
        Assert.Equal("test/route", section.Route);
        Assert.Equal("extra", section.AdditionalData);
        Assert.Equal(1, section.Limit);
        Assert.Same(payloadMarker, section.OriginalPayload);
        Assert.Null(((IHomeScreenSection)section).TranslationMetadata);
    }

    [Fact]
    public void GetResults_forwards_payload_to_registered_delegate()
    {
        HomeScreenSectionPayload? receivedPayload = null;
        QueryResult<BaseItemDto> expected = new QueryResult<BaseItemDto>([new BaseItemDto { Id = Guid.NewGuid() }]);

        PluginDefinedSection section = new PluginDefinedSection("uuid", "Display")
        {
            OnGetResults = payload =>
            {
                receivedPayload = payload;
                return expected;
            }
        };

        HomeScreenSectionPayload payload = new HomeScreenSectionPayload { UserId = Guid.NewGuid(), AdditionalData = "data" };
        QueryResult<BaseItemDto> result = section.GetResults(payload, new FakeQueryCollection());

        Assert.Same(expected, result);
        Assert.Same(payload, receivedPayload);
    }

    [Fact]
    public void CreateInstances_always_yields_itself()
    {
        PluginDefinedSection section = MakeSection();

        for (int count = 0; count <= 3; count++)
        {
            List<IHomeScreenSection> instances = [.. section.CreateInstances(Guid.NewGuid(), count)];
            Assert.Single(instances);
            Assert.Same(section, instances[0]);
        }
    }

    [Fact]
    public void GetInfo_maps_all_fields_with_landscape_view_mode()
    {
        object payloadMarker = new object();
        PluginDefinedSection section = MakeSection(originalPayload: payloadMarker);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("test-uuid", info.Section);
        Assert.Equal("Test Section", info.DisplayText);
        Assert.Equal("extra", info.AdditionalData);
        Assert.Equal("test/route", info.Route);
        Assert.Equal(1, info.Limit);
        Assert.Same(payloadMarker, info.OriginalPayload);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
    }

    [Fact]
    public void AsInfo_extension_delegates_to_GetInfo()
    {
        PluginDefinedSection section = MakeSection();

        HomeScreenSectionInfo info = HomeScreenSectionExtensions.AsInfo(section);

        Assert.Equal(section.Section, info.Section);
        Assert.Equal(section.DisplayText, info.DisplayText);
    }

    private static PluginDefinedSection MakeSection(object? originalPayload = null)
    {
        return new PluginDefinedSection(
            "test-uuid",
            "Test Section",
            route: "test/route",
            additionalData: "extra",
            originalPayload: originalPayload)
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
    }
}
