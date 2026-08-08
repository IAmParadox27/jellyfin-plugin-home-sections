using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

/// <summary>
/// Public handler discovered by PluginInterface through the loaded-assembly reflection
/// path. Must stay public with a parameterless constructor for ActivatorUtilities.
/// </summary>
public class PluginInterfaceTestResultsHandler
{
    public static QueryResult<BaseItemDto> BuildResults(HomeScreenSectionPayload payload)
    {
        return new QueryResult<BaseItemDto>(
        [
            new BaseItemDto { Id = Guid.NewGuid(), Name = "From Reflection Handler", Overview = payload.AdditionalData }
        ]);
    }
}

[Collection("Plugin Instance")]
public class PluginInterfaceTests
{
    private readonly PluginFixture _fixture;

    public PluginInterfaceTests(PluginFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void RegisterSection_invokes_reflection_handler_from_loaded_assembly()
    {
        PluginDefinedSection? captured = null;
        _fixture.HomeScreenManagerMock
            .Setup(manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()))
            .Callback<PluginDefinedSection>(section => captured = section);

        JObject payload = JObject.Parse($$"""
            {
                "id": "reflected-section",
                "displayText": "Reflected",
                "additionalData": "marker",
                "resultsAssembly": "{{typeof(PluginInterfaceTestResultsHandler).Assembly.FullName}}",
                "resultsClass": "{{typeof(PluginInterfaceTestResultsHandler).FullName}}",
                "resultsMethod": "BuildResults"
            }
            """);

        PluginInterface.RegisterSection(payload);

        Assert.NotNull(captured);
        Assert.Equal("reflected-section", captured!.Section);

        QueryResult<BaseItemDto> results = captured.GetResults(
            new HomeScreenSectionPayload { UserId = Guid.NewGuid(), AdditionalData = "marker" },
            new FakeQueryCollection());

        Assert.Equal("From Reflection Handler", Assert.Single(results.Items).Name);
    }

    [Fact]
    public void RegisterSection_posts_to_results_endpoint_when_no_assembly_given()
    {
        QueryResult<BaseItemDto> serverResult = new QueryResult<BaseItemDto>(
        [
            new BaseItemDto { Id = Guid.NewGuid(), Name = "From Endpoint" }
        ]);

        using JellyseerrFakeServer server = JellyseerrFakeServer.Start(
            path => (200, JsonConvert.SerializeObject(serverResult)));

        int port = new Uri(server.BaseUrl).Port;
        _fixture.ServerApplicationHostMock
            .SetupGet(host => host.HttpPort)
            .Returns(port);

        PluginDefinedSection? captured = null;
        _fixture.HomeScreenManagerMock
            .Setup(manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()))
            .Callback<PluginDefinedSection>(section => captured = section);

        JObject payload = JObject.Parse("""
            {
                "id": "endpoint-section",
                "displayText": "Endpoint",
                "resultsEndpoint": "/sections/results"
            }
            """);

        PluginInterface.RegisterSection(payload);

        Assert.NotNull(captured);
        QueryResult<BaseItemDto> results = captured!.GetResults(
            new HomeScreenSectionPayload { UserId = Guid.NewGuid() },
            new FakeQueryCollection());

        Assert.Equal("From Endpoint", Assert.Single(results.Items).Name);
    }

    [Fact]
    public void RegisterSection_without_handler_returns_empty_results()
    {
        PluginDefinedSection? captured = null;
        _fixture.HomeScreenManagerMock
            .Setup(manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()))
            .Callback<PluginDefinedSection>(section => captured = section);

        JObject payload = JObject.Parse("""
            {
                "id": "no-handler-section",
                "displayText": "No Handler"
            }
            """);

        PluginInterface.RegisterSection(payload);

        Assert.NotNull(captured);
        QueryResult<BaseItemDto> results = captured!.GetResults(
            new HomeScreenSectionPayload { UserId = Guid.NewGuid() },
            new FakeQueryCollection());

        Assert.Empty(results.Items);
    }

    [Fact]
    public void RegisterSection_preserves_object_original_payload()
    {
        PluginDefinedSection? captured = null;
        _fixture.HomeScreenManagerMock
            .Setup(manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()))
            .Callback<PluginDefinedSection>(section => captured = section);

        JObject payload = JObject.Parse("""
            {
                "id": "payload-section",
                "displayText": "Payload",
                "originalPayload": { "foo": "bar" }
            }
            """);

        PluginInterface.RegisterSection(payload);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.OriginalPayload);
        JObject preserved = Assert.IsType<JObject>(captured.OriginalPayload);
        Assert.Equal("bar", preserved.Value<string>("foo"));
    }
}
