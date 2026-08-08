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

        /// <summary>
        /// Person item used as the section title link target.
        /// </summary>
        public object? OriginalPayload { get; set; }
        
        protected abstract IReadOnlyList<string> PersonTypes { get; }

        protected abstract int MinRequiredItems { get; }

        public virtual TranslationMetadata? TranslationMetadata { get; protected set; }
        
        protected ILibraryManager _libraryManager { get; }
        protected IDtoService _dtoService { get; }
        protected IUserManager _userManager { get; }

        protected PersonsSectionBase(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _dtoService = dtoService;
            _userManager = userManager;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = _userManager.GetUserById(payload.UserId);
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio],
                ImageTypeLimit = 1,
                ImageTypes = [ImageType.Thumb,
                    ImageType.Backdrop,
                    ImageType.Primary,]
            };
            Guid personId = Guid.Parse(payload.AdditionalData ?? Guid.Empty.ToString());
            
            VirtualFolderInfo[] folders = _libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(_libraryManager, user);

            List<BaseItem> personItems = folders.SelectMany(x => _libraryManager.GetItemList(new InternalItemsQuery()
            {
                PersonIds = [personId],
                PersonTypes = PersonTypes.ToArray(),
                OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
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
            }).DistinctBy(x => x.Id).ToList();
            
            return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(personItems, dtoOptions, user));
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            User? user = _userManager.GetUserById(userId ?? Guid.Empty);
            // Want to use the user data at some point to actually weight the people chosen based on watch history, similar to how Genres are picked.
            // For now this is fine to get something in.
            List<Person> people = _libraryManager.GetPeopleItems(new InternalPeopleQuery(PersonTypes, [])).ToList();

            people.Shuffle();

            List<IHomeScreenSection> sections = [];
            
            VirtualFolderInfo[] folders = _libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(_libraryManager, user);

            foreach (Person person in people)
            {
                List<BaseItem> personItems = folders.SelectMany(x => _libraryManager.GetItemList(new InternalItemsQuery()
                {
                    PersonIds = [person.Id],
                    PersonTypes = PersonTypes.ToArray(),
                    IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
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