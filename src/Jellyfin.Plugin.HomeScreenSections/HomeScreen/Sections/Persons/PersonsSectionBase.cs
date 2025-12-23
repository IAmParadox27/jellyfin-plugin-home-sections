using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons
{
    public abstract class PersonsSectionBase : IHomeScreenSection
    {
        public abstract string? Section { get; }
        
        public abstract string? DisplayText { get; set; }
        
        public int? Limit => 5;
        
        public string? Route => null;
        
        public string? AdditionalData { get; set; }
        
        public object? OriginalPayload => null;
        
        protected abstract IReadOnlyList<string> PersonTypes { get; }

        protected abstract int MinRequiredItems { get; }

        public virtual TranslationMetadata? TranslationMetadata { get; protected set; } = null;
        
        protected readonly ILibraryManager m_libraryManager;
        protected readonly IDtoService m_dtoService;
        protected readonly IUserManager m_userManager;

        public PersonsSectionBase(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager)
        {
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            m_userManager = userManager;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = m_userManager.GetUserById(payload.UserId);
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio
                },
                ImageTypeLimit = 1,
                ImageTypes = new List<ImageType>
                {
                    ImageType.Thumb,
                    ImageType.Backdrop,
                    ImageType.Primary,
                }
            };
            Guid personId = Guid.Parse(payload.AdditionalData ?? Guid.Empty.ToString());
            
            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(m_libraryManager, user);

            IReadOnlyList<BaseItem> personItems = folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery()
            {
                PersonIds = new[] { personId },
                PersonTypes = PersonTypes.ToArray(),
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Ascending) },
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                Limit = 16,
                ParentId = Guid.Parse(x.ItemId),
                Recursive = true
            })).DistinctBy(x => x.Id).Select(x =>
            {
                if (x is Episode episode)
                {
                    return episode.Series;
                }

                return x;
            }).DistinctBy(x => x.Id).ToArray();
            
            return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(personItems, dtoOptions, user));
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);
            // Want to use the user data at some point to actually weight the people chosen based on watch history, similar to how Genres are picked.
            // For now this is fine to get something in.
            List<Person> people = m_libraryManager.GetPeopleItems(new InternalPeopleQuery(PersonTypes, Array.Empty<string>())).ToList();

            people.Shuffle();

            List<IHomeScreenSection> sections = new List<IHomeScreenSection>();
            
            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(m_libraryManager, user);

            foreach (Person person in people)
            {
                IReadOnlyList<BaseItem> personItems = folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery()
                {
                    PersonIds = new[] { person.Id },
                    PersonTypes = PersonTypes.ToArray(),
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                    ParentId = Guid.Parse(x.ItemId),
                    Recursive = true,
                    Limit = 16
                })).DistinctBy(x => x.Id).Select(x =>
                {
                    if (x is Episode episode)
                    {
                        return episode.Series;
                    }

                    return x;
                }).DistinctBy(x => x.Id).ToList();

                if (personItems.Count >= MinRequiredItems)
                {
                    sections.Add(CreateInstance(person));
                }

                if (sections.Count == instanceCount)
                {
                    break;
                }
            }
            
            return sections;
        }

        protected abstract IHomeScreenSection CreateInstance(Person person);

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
                ViewMode = SectionViewMode.Landscape,
                AllowHideWatched = true
            };
        }
    }
}