using System.Diagnostics;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
	public class BecauseYouWatchedSection : IHomeScreenSection
	{
		public string? Section => "BecauseYouWatched";

		public string? DisplayText { get; set; } = "Because You Watched";

		public int? Limit => 5;

		public string? Route => null;

		public string? AdditionalData { get; set; }

		public object? OriginalPayload => null;
		
		private IUserDataManager UserDataManager { get; set; }
		private IUserManager UserManager { get; set; }
		private ILibraryManager LibraryManager { get; set; }
		private IDtoService DtoService { get; set; }
		private ICollectionManager CollectionManager { get; set; }
		private CollectionManagerProxy CollectionManagerProxy { get; set; }

		public BecauseYouWatchedSection(IUserDataManager userDataManager, IUserManager userManager, ILibraryManager libraryManager, 
			IDtoService dtoService, ICollectionManager collectionManager, CollectionManagerProxy collectionProxy)
		{
			UserDataManager = userDataManager;
			UserManager = userManager;
			LibraryManager = libraryManager;
			DtoService = dtoService;
			CollectionManager = collectionManager;
			CollectionManagerProxy = collectionProxy;
		}

		public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
		{
			User? user = userId is null || userId.Value.Equals(default)
				? null
				: UserManager.GetUserById(userId.Value);

			DtoOptions? dtoOptions = new DtoOptions 
			{ 
				Fields = new[] 
				{ 
					ItemFields.PrimaryImageAspectRatio, 
					ItemFields.MediaSourceCount
				}
			};

			VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
				.Where(x => x.CollectionType == CollectionTypeOptions.movies)
				.ToArray();

			List<BaseItem>? recentlyPlayedMovies = folders.SelectMany(x =>
			{
				InternalItemsQuery? query = new InternalItemsQuery(user)
				{
					IncludeItemTypes = new[]
					{
						BaseItemKind.Movie
					},
					OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending), (ItemSortBy.Random, SortOrder.Descending) },
					Limit = 15,
					ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
					Recursive = true,
					IsPlayed = true,
					DtoOptions = dtoOptions
				};

				return LibraryManager.GetItemList(query);
			}).ToList();
			
			recentlyPlayedMovies.Shuffle();
			
			List<BaseItem> pickedMovies = new List<BaseItem>();

			Queue<BaseItem> queue = new Queue<BaseItem>(recentlyPlayedMovies);
			while (pickedMovies.Count < instanceCount && queue.Count > 0)
			{
				BaseItem elementToConsider = queue.Dequeue();
				
				if (user != null)
				{
					var collections = CollectionManagerProxy.GetCollections(user)
						.Select(y => (y, y.GetChildren(user, true, null)))
						.Where(y => y.Item2
							.OfType<Movie>().Contains(elementToConsider as Movie));

					bool isPicked = false;
					foreach ((BoxSet Item, IEnumerable<BaseItem> Children) collection in collections)
					{
						if (collection.Children.OfType<Movie>().Any(y => pickedMovies?.Select(z => z.Id).Contains(y.Id) ?? true))
						{
							isPicked = true;
							break;
						}
					}

					if (isPicked)
					{
						continue;
					}
				}

				pickedMovies.Add(elementToConsider);
				yield return new BecauseYouWatchedSection(UserDataManager, UserManager, LibraryManager, DtoService, CollectionManager, CollectionManagerProxy)
				{
					AdditionalData = elementToConsider.Id.ToString(),
					DisplayText = "Because You Watched " + elementToConsider.Name
				};
			}
		}

		public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
		{
			Stopwatch sw = Stopwatch.StartNew();
			User user = UserManager.GetUserById(payload.UserId)!;
			
			DtoOptions? dtoOptions = new DtoOptions
			{
				Fields = new[]
				{
					ItemFields.PrimaryImageAspectRatio,
					ItemFields.MediaSourceCount
				},
				ImageTypes = new[]
				{
					ImageType.Thumb,
					ImageType.Backdrop,
					ImageType.Primary,
				},
				ImageTypeLimit = 1
			};

			BaseItem? item = LibraryManager.GetItemById(Guid.Parse(payload.AdditionalData ?? Guid.Empty.ToString()));

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
			var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

            VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
	            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
	            .ToArray();
            
            IList<BaseItem>? similar = folders.SelectMany(x =>
            {
	            var items = LibraryManager.GetItemList(new InternalItemsQuery
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
	            }.ApplySimilarSettings(item));

	            return items;
            }).ToList();
            
            similar.Shuffle();
            
			return new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(similar.Take(16).ToArray(), dtoOptions, user));
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
				ViewMode = SectionViewMode.Landscape,
                AllowHideWatched = true
			};
		}
	}
}
