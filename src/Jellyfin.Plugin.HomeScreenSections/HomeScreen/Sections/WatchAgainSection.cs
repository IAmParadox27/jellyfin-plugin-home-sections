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
		class EpisodeEqualityComparer : IEqualityComparer<Episode?>
		{
			public bool Equals(Episode? x, Episode? y)
			{
				if (x == null && y == null)
				{
					return false;
				}

				return x?.Id == y?.Id;
			}

			public int GetHashCode([DisallowNull] Episode obj)
			{
				return obj.GetHashCode();
			}
		}

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
				IEnumerable<BaseItem> collections = CollectionManagerProxy.GetCollections(user)
					.Where(x => x.IsPlayedVersionSpecific(user))
					.Select(x =>
					{
						IReadOnlyList<BaseItem>? children = x.GetChildren(user, true, null);

						if (children.Count(y => y is Movie) > 1)
						{
							return children.OfType<Movie>().OrderBy(y => y.PremiereDate).First();
						}

						return null;
					})
					.Where(x => x != null)
					//.Where(x =>
					//{
					//	UserItemData data = UserDataManager.GetUserData(user.Id, x);
					//
					//	return data.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28));
					//})
					.Cast<BaseItem>();

				results.AddRange(collections.ToList());
			}

			{

				IEnumerable<Series>? series = LibraryManager.GetItemList(new InternalItemsQuery
				{
					IncludeItemTypes = new[] { BaseItemKind.Series }
				}).Cast<Series>().Where(x => x.IsPlayedVersionSpecific(user));
				EpisodeEqualityComparer? eqComp = new EpisodeEqualityComparer();

				IEnumerable<BaseItem?> firstEpisodes = series
				//.Where(x =>
				//{
				//	UserItemData data = UserDataManager.GetUserData(user.Id, x);
				//
				//	return data.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28));
				//})
				.Select(x =>
				{
					return x.GetChildren(user, true, null).Cast<Season>().Where(y => y.IndexNumber == 1).FirstOrDefault()?.GetChildren(user, true, null).Cast<Episode>().Where(y => y.IndexNumber == 1 && !y.IsMissingEpisode).FirstOrDefault();
				}).Distinct(eqComp);

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
