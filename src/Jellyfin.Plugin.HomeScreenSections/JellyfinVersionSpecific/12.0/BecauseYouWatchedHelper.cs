using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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

            ISimilarItemsManager similarItemsManager = section.ServiceProvider.GetRequiredService<ISimilarItemsManager>();
            IReadOnlyList<BaseItem> similarItems = await similarItemsManager.GetSimilarItemsAsync(item, Array.Empty<Guid>().ToList(), user, dtoOptions, limit, libraryOptions, cancellationToken);

            similarItems = similarItems.Where(x =>
            {
                if (isPlayed == null)
                {
                    return true;
                }

                bool isItemPlayed = x.IsPlayedVersionSpecific(user);

                return isPlayed == isItemPlayed;
            }).ToList();
            
            return similarItems;
        }
    }
}