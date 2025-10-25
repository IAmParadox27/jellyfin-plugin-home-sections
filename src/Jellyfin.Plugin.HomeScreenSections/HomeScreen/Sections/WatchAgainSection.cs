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
				var boxSets = LibraryManager.GetItemList(new InternalItemsQuery(user)
				{
					IncludeItemTypes = new[]
					{
						BaseItemKind.BoxSet
					}
				}).OfType<BoxSet>();
				
				var collections = boxSets.Select(x =>
				{
					IReadOnlyList<BaseItem>? children = x.GetChildren(user, true, new InternalItemsQuery(user)
					{
						Recursive = true
					});
					
					if (!children.All(y => y.IsPlayedVersionSpecific(user)))
					{
						return null;
					}
					
					if (children.Count(y => y is Movie) > 1)
					{
						return children.OfType<Movie>().OrderBy(y => y.PremiereDate).First();
					}
				
					return null;
				})
				.Where(x => x != null)
				.Where(x =>
				{
					UserItemData data = UserDataManager.GetUserData(user, x!);
				
					return data.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28));
				})
				.Cast<BaseItem>();

				results.AddRange(collections.ToList());
			}

			{
				IEnumerable<Series>? series = LibraryManager.GetItemList(new InternalItemsQuery(user)
				{
					IncludeItemTypes = new[]
					{
						BaseItemKind.Series
					}
				}).Cast<Series>().Where(x =>
				{
					var episodes = x.GetEpisodes(user, dtoOptions, false);

					return episodes.All(y =>
					{
						bool isPlayed = y.IsPlayedVersionSpecific(user);

						if (isPlayed)
						{
							return UserDataManager.GetUserData(user, x)?.LastPlayedDate < DateTime.Now.Subtract(TimeSpan.FromDays(28));
						}
						
						return false;
					});
				}).ToList();
				
				IEnumerable<BaseItem?> firstEpisodes = series
				.Select(x => x.GetEpisodes(user, dtoOptions, false).Cast<Episode>()
					.OrderBy(x => x.PremiereDate)
				.FirstOrDefault())
				.Where(x => x != null).DistinctBy(x => x!.Id);

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
