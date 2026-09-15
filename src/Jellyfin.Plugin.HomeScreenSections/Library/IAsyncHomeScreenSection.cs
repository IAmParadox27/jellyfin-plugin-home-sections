using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.Library
{
    // Optional support for cancellable built-in requests. The reflection-facing synchronous interface is unchanged.
    public interface IAsyncHomeScreenSection
    {
        Task<QueryResult<BaseItemDto>> GetResultsAsync(HomeScreenSectionPayload payload, IQueryCollection queryCollection, CancellationToken cancellationToken);
    }
}
