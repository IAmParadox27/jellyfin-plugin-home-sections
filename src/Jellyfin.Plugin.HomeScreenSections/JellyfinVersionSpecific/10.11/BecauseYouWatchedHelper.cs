using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Identity;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class BecauseYouWatchedHelper
    {
        public static InternalItemsQuery ApplySimilarSettings(this InternalItemsQuery query, BaseItem item)
        {
            query.Genres = item.Genres;
            query.Tags = item.Tags;

            return query;
        }
        
        public static async Task<IReadOnlyList<BaseItem>> GetSimilarItems(this BecauseYouWatchedSection section, BaseItem item, DtoOptions dtoOptions, User? user = null, int? limit = null, LibraryOptions? libraryOptions = null, CancellationToken cancellationToken = default)
        {
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == section.Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;
            
            VirtualFolderInfo[] folders = section.LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.IsMixedFolder(section.LibraryManager))
                .FilterToUserPermitted(section.LibraryManager, user);
            
            IList<BaseItem>? similar = folders.SelectMany(x =>
            {
                var item = section.LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = section.LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        BaseItemKind.Movie
                    },
                    OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
                    User = user,
                    IsPlayed = isPlayed,
                    DtoOptions = dtoOptions,
                    Limit = 24,
                    Recursive = true,
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                }.ApplySimilarSettings(item)).Items;
            }).ToList();
            
            // Scoring system to prefer more similar titles
            var scoredSimilar = similar.Select(x =>
            {
                int sharedGenreWeight = 5;
	            
                int sharedTags = x.Tags.Count(y => item.Tags.Contains(y));
                int sharedGenres = x.Genres.Count(y => item.Genres.Contains(y));

                if (sharedGenres == 0)
                {
                    return new
                    {
                        Item = x,
                        Score = 0
                    };
                }
	            
                return new
                {
                    Item = x,
                    Score = (sharedGenres * sharedGenreWeight) + sharedTags 
                };
            }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(24).ToList();
            
            scoredSimilar.Shuffle();
            
            return scoredSimilar.Select(x => x.Item).ToList();
        }
    }
}