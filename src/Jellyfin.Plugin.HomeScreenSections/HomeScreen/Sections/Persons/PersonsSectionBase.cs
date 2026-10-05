using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
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
        
        public string? Route { get; protected set; }
        
        public string? AdditionalData { get; set; }
        
        public object? OriginalPayload { get; protected set; }
        
        protected abstract IReadOnlyList<string> PersonTypes { get; }

        protected abstract int MinRequiredItems { get; }

        // Upper bound on how many candidate people are probed per request on the 10.11 fallback below.
        private const int MaxCandidatesToScan = 200;

        public virtual TranslationMetadata? TranslationMetadata { get; protected set; } = null;
        
        protected readonly ILibraryManager m_libraryManager;
        protected readonly IDtoService m_dtoService;
        protected readonly IUserManager m_userManager;
        protected readonly PerUserComputedStatsCache m_statsCache;

        public PersonsSectionBase(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager, PerUserComputedStatsCache statsCache)
        {
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            m_userManager = userManager;
            m_statsCache = statsCache;
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

            // Expensive to redo every load - cache it per user instead.
            // Keyed per concrete section (Starring vs Directed By) since they scan different person types.
            if (!m_statsCache.TryGetOrCompute(
                userId ?? Guid.Empty,
                Section + "-qualifying-people",
                TimeSpan.FromHours(24),
                () => ComputeQualifyingPeople(user),
                out List<(Guid Id, string Name)> qualifyingPeople,
                isCacheable: ids => ids.Count > 0,
                clearOnUserDataChange: false))
            {
                return Array.Empty<IHomeScreenSection>();
            }

            if (qualifyingPeople.Count == 0)
            {
                return Array.Empty<IHomeScreenSection>();
            }

            List<(Guid Id, string Name)> shuffledPeople = new List<(Guid Id, string Name)>(qualifyingPeople);
            shuffledPeople.Shuffle();

            return shuffledPeople.Take(instanceCount).Select(x => CreateLinkedInstance(x.Id, x.Name, user)).ToList();
        }

        private IHomeScreenSection CreateLinkedInstance(Guid fallbackId, string name, User? user)
        {
            // Item queries filter by the person's library item id, not the people table id, so resolve it from the name.
            Person? person = m_libraryManager.GetPerson(name);

            PersonsSectionBase instance = CreateInstance(person?.Id ?? fallbackId, name);

            if (person != null)
            {
                instance.Route = "originalpayload";
                instance.OriginalPayload = m_dtoService.GetBaseItemDto(person, new DtoOptions(), user);
            }

            return instance;
        }

        private List<(Guid Id, string Name)> ComputeQualifyingPeople(User? user)
        {
            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(m_libraryManager, user);

            if (m_libraryManager.SupportsBatchPeopleByItems())
            {
                return ComputeQualifyingPeopleViaBatchLookup(folders);
            }

            // Want to use the user data at some point to actually weight the people chosen based on watch history, similar to how Genres are picked.
            // For now this is fine to get something in.
            List<Person> people = m_libraryManager.GetPeopleItems(new InternalPeopleQuery(PersonTypes, Array.Empty<string>())).QueryResultToList<BaseItem, Person>();

            people.Shuffle();
            people = people.Take(MaxCandidatesToScan).ToList();

            List<(Guid Id, string Name)> qualifying = new List<(Guid Id, string Name)>();

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
                    qualifying.Add((person.Id, person.Name));
                }
            }

            return qualifying;
        }

        // Jellyfin 12+ fast path: batch-fetch every accessible movie/episode's
        // credits in one call and aggregate locally, instead of running one
        // items query per candidate person.
        private List<(Guid Id, string Name)> ComputeQualifyingPeopleViaBatchLookup(VirtualFolderInfo[] folders)
        {
            List<BaseItem> mediaItems = folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery()
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                ParentId = Guid.Parse(x.ItemId),
                Recursive = true,
                DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
            })).DistinctBy(x => x.Id).ToList();

            Dictionary<Guid, Guid> collapseKeyByItemId = mediaItems.ToDictionary(
                x => x.Id,
                x => x is Episode episode ? (episode.Series?.Id ?? x.Id) : x.Id);

            IReadOnlyDictionary<Guid, IReadOnlyList<PersonInfo>> peopleByItem = m_libraryManager.GetPeopleByItemsVersionSpecific(
                mediaItems.Select(x => x.Id).ToList());

            Dictionary<Guid, HashSet<Guid>> itemsByPerson = new Dictionary<Guid, HashSet<Guid>>();
            Dictionary<Guid, string> namesByPerson = new Dictionary<Guid, string>();

            foreach ((Guid itemId, IReadOnlyList<PersonInfo> credits) in peopleByItem)
            {
                if (!collapseKeyByItemId.TryGetValue(itemId, out Guid collapseKey))
                {
                    continue;
                }

                foreach (PersonInfo credit in credits)
                {
                    if (!PersonTypes.Contains(credit.Type.ToString()))
                    {
                        continue;
                    }

                    if (!itemsByPerson.TryGetValue(credit.Id, out HashSet<Guid>? items))
                    {
                        items = new HashSet<Guid>();
                        itemsByPerson[credit.Id] = items;
                    }

                    items.Add(collapseKey);
                    namesByPerson[credit.Id] = credit.Name;
                }
            }

            return itemsByPerson.Where(kvp => kvp.Value.Count >= MinRequiredItems).Select(kvp => (kvp.Key, namesByPerson[kvp.Key])).ToList();
        }

        protected abstract PersonsSectionBase CreateInstance(Guid personId, string personName);

        protected abstract string AdminTranslationKey { get; }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                AdminTranslationKey = AdminTranslationKey,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Landscape,
                AllowHideWatched = true,
                PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}