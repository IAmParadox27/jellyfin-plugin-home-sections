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
    public class DiscoverTvSection : DiscoverSection
    {
        public override string? Section => "DiscoverTV";

        public override string? DisplayText { get; set; } = "Discover TV Shows";
        
        protected override string JellyseerEndpoint => "/api/v1/discover/tv";
        
        public DiscoverTvSection(IUserManager userManager) : base(userManager)
        {
        }
    }
}