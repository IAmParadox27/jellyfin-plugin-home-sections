using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class HomeScreenBootstrap
    {
        public bool Enabled { get; set; }

        public bool AllowUserOverride { get; set; }

        public bool PaginationEnabled { get; set; }

        public int NumResultsPerPage { get; set; }

        public QueryResult<HomeScreenSectionInfo>? Sections { get; set; }
    }
}
