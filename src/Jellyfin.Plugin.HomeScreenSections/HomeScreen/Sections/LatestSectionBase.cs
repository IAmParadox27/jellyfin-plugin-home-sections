﻿using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public abstract class LatestSectionBase : IHomeScreenSection
    {
        public abstract string? Section { get; }
        public abstract string? DisplayText { get; set; }
        public virtual int? Limit => 1;
        public virtual string? Route { get; } = null;
        public virtual string? AdditionalData { get; set; }
        public virtual object? OriginalPayload { get; set; } = null;
        public abstract SectionViewMode DefaultViewMode { get; }
        
        protected abstract BaseItemKind SectionItemKind { get; }
        
        protected abstract CollectionType CollectionType { get; }
        
        protected abstract string? LibraryId { get; }
        
        protected readonly IUserViewManager m_userViewManager;
        protected readonly IUserManager m_userManager;
        protected readonly ILibraryManager m_libraryManager;
        protected readonly IDtoService m_dtoService;
        protected readonly IServiceProvider m_serviceProvider;
        
        public LatestSectionBase(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            
            m_serviceProvider = serviceProvider;
        }

        public virtual QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path
                },
                EnableImages = true
            };

            dtoOptions.ImageTypeLimit = 1;
            dtoOptions.ImageTypes = new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary,
            };
            
            User? user = m_userManager.GetUserById(payload.UserId);

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

            IReadOnlyList<BaseItem> latestMovies = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    SectionItemKind
                },
                Limit = 16,
                OrderBy = new[]
                {
                    (ItemSortBy.PremiereDate, SortOrder.Descending)
                },
                IsPlayed = isPlayed
            });

            return new QueryResult<BaseItemDto>(Array.ConvertAll(latestMovies.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            // Get only collection folders for the section type that the user can access
            var libraryFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .Where(x => (x as ICollectionFolder)?.CollectionType == CollectionType)
                .ToArray();
            
            // Check if there's a configured default library, otherwise use first available
            var folder = !string.IsNullOrEmpty(LibraryId)
                ? libraryFolders.FirstOrDefault(x => x.Id.ToString() == LibraryId)
                : null;
            
            // Fall back to first movies library if no configured library found
            folder ??= libraryFolders.FirstOrDefault();
            
            if (folder != null)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];
                
                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
            }

            LatestSectionBase sectionBase = CreateInstance();
            sectionBase.DisplayText = DisplayText;
            sectionBase.AdditionalData = AdditionalData;
            sectionBase.OriginalPayload = originalPayload;

            return sectionBase;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = DefaultViewMode,
                AllowHideWatched = true
            };
        }
        
        protected abstract LatestSectionBase CreateInstance();
    }
}