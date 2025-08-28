using System.Net.Http.Json;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class DiscoverMoviesSection : DiscoverSection
    {
        public override string? Section => "DiscoverMovies";

        public override string? DisplayText { get; set; } = "Discover Movies";

        protected override string JellyseerEndpoint => "/api/v1/discover/movies";

        public DiscoverMoviesSection(IUserManager userManager) : base(userManager)
        {
        }
    }
}