using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.HomeScreenSections.Attributes;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
	internal class WatchAgainSection : IHomeScreenSection
	{
		public string? Section => "WatchAgain";

		public string? DisplayText { get; set; } = "Watch It Again";

		public int? Limit => 1;

		public string? Route => null;

		public string? AdditionalData { get; set; }

		public object? OriginalPayload => null;
		
		private ICollectionManager CollectionManager { get; set; }

		private IUserManager UserManager { get; set; }

		private IDtoService DtoService { get; set; }

		private IUserDataManager UserDataManager { get; set; }

		private ITVSeriesManager TVSeriesManager { get; set; }

		private ILibraryManager LibraryManager { get; set; }
		
		private CollectionManagerProxy CollectionManagerProxy { get; set; }

		public WatchAgainSection(
			ICollectionManager collectionManager, 
			IUserManager userManager, 
			IDtoService dtoService, 
			IUserDataManager userDataManager, 
			ITVSeriesManager tvSeriesManager, 
			ILibraryManager libraryManager,
			CollectionManagerProxy collectionManagerProxy)
		{
			CollectionManager = collectionManager;
			UserManager = userManager;
			DtoService = dtoService;
			UserDataManager = userDataManager;
			TVSeriesManager = tvSeriesManager;
			LibraryManager = libraryManager;
			CollectionManagerProxy = collectionManagerProxy;
		}

		public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
		{
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

			User user = UserManager.GetUserById(payload.UserId)!;
			
			List<BaseItem> results = new List<BaseItem>();

			{
				VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
					.Where(x => x.CollectionType == CollectionTypeOptions.boxsets)
					.ToArray();
				
				var boxSets = folders.SelectMany(x =>
				{
					return LibraryManager.GetItemList(new InternalItemsQuery(user)
					{
						ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
						Recursive = true,
						IncludeItemTypes = new []
						{
							BaseItemKind.BoxSet
						}
					});
				}).OfType<BoxSet>().ToArray();
				
				var collections = boxSets.Select(x =>
				{
					(BaseItem Item, UserItemData? UserData)[] children = x.GetChildren(user, true, new InternalItemsQuery(user)).Select(y => (y, UserDataManager.GetUserData(user, y))).ToArray();
					
					if (!children.All(y => y.UserData?.Played ?? false))
					{
						return (null, null);
					}
					
					if (children.Count(y => y.Item is Movie) > 1)
					{
						return children.OrderBy(y => y.Item.PremiereDate).First(y => y.Item is Movie);
					}
				
					return (null, null);
				})
				.Where(x => x.Item != null)
				.Where(x => x.UserData?.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28))).ToArray();

				results.AddRange(collections.Select(x => x.Item).ToArray()!);
			}

			{
				VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
					.Where(x => x.CollectionType == CollectionTypeOptions.tvshows)
					.ToArray();

				IEnumerable<Series>? series = folders.SelectMany(x =>
				{
					return LibraryManager.GetItemList(new InternalItemsQuery(user)
					{
						IncludeItemTypes = new[]
						{
							BaseItemKind.Series
						},
						ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
						Recursive = true,
					}).Cast<Series>();
				}).Where(x =>
				{
					var episodes = x.GetEpisodes(user, dtoOptions, false);

					return episodes.All(y =>
					{
						var userData = UserDataManager.GetUserData(user, x);
						
						return (userData?.Played ?? false) && userData?.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28));
					});
				}).ToList();
				
				IEnumerable<BaseItem?> firstEpisodes = series
				.Select(x => x.GetEpisodes(user, dtoOptions, false).Cast<Episode>()
					.OrderBy(x => x.PremiereDate)
				.FirstOrDefault())
				.Where(x => x != null).DistinctBy(x => x!.Id).ToArray();

				results.AddRange(firstEpisodes.Where(x => x != null).Cast<BaseItem>());
			}
			
			results = results.OrderBy(x =>
			{
				UserItemData data = UserDataManager.GetUserData(user, x);
			
				return data.LastPlayedDate;
			}).Take(16).ToList();
			
			QueryResult<BaseItemDto>? result = new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(results, dtoOptions, user));

			return result;
		}

		public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
		{
			return this;
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
				ViewMode = SectionViewMode.Landscape
			};
		}
	}
}
