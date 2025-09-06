using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
	internal class MyListSection : IHomeScreenSection
	{
		public string? Section => "MyList";

		public string? DisplayText { get; set; } = "My List";

		public int? Limit => 1;

		public string? Route { get; set; } = null;

		public string? AdditionalData { get; set; }

		public object? OriginalPayload { get; set; } = null;
		
		private IUserManager UserManager { get; set; }

		private IDtoService DtoService { get; set; }

		private IPlaylistManager PlaylistManager { get; set; }

		private IUserDataManager UserDataManager { get; set; }

		public MyListSection(IUserManager userManager, IDtoService dtoService, IPlaylistManager playlistManager, IUserDataManager userDataManager)
		{
			UserManager = userManager;
			DtoService = dtoService;
			PlaylistManager = playlistManager;
			UserDataManager = userDataManager;
		}	
		public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
		{
			string playlistId = GetPlaylistId(userId, "My List");
			
			// Use the playlist name as display text, defaulting to "My List"
			string displayText = "My List";
			
			// If user has a playlist, route to the specific playlist details
			if (!string.IsNullOrEmpty(playlistId))
			{
				return new MyListSection(UserManager, DtoService, PlaylistManager, UserDataManager)
				{
					DisplayText = displayText,
					Route = "details",
					OriginalPayload = new { Id = playlistId, Type = "Playlist" }
				};
			}
			
			// If no playlist, use simple list route to playlists library
			return new MyListSection(UserManager, DtoService, PlaylistManager, UserDataManager)
			{
				DisplayText = displayText,
				Route = "list"
			};
		}

        private IEnumerable<BaseItem> ApplySortingToItems(IEnumerable<BaseItem> items, string sortOrder, string sortDirection, User user)
        {
            var sortedItems = sortOrder switch
            {
                "Default" => items,
                "PremiereDate" => items.OrderBy(item => item.PremiereDate ?? DateTime.MinValue),
                "DateAdded" => items.OrderBy(item => item.DateCreated),
                "Alphabetical" => items.OrderBy(item => item.SortName ?? item.Name),
                "RecentlyWatched" => GetRecentlyWatchedSortedItems(items, user),
                "CommunityRating" => items.OrderBy(item => item.CommunityRating ?? 0),
                "Random" => items.OrderBy(x => Random.Shared.Next()),
                _ => items
            };

            if (string.Equals(sortDirection, "Descending", StringComparison.OrdinalIgnoreCase))
            {
                sortedItems = sortedItems.Reverse();
            }

            return sortedItems;
        }

		/// <summary>
		/// Recently watched sorting with pre-loaded user data
		/// </summary>
		private IEnumerable<BaseItem> GetRecentlyWatchedSortedItems(IEnumerable<BaseItem> items, User user)
		{
			var itemsList = items.ToList();
			var userDataLookup = new Dictionary<Guid, DateTime>();
			
			// Pre-load all user data to avoid repeated lookups
			foreach (var item in itemsList)
			{
				try
				{
					var userData = UserDataManager.GetUserData(user, item);
					userDataLookup[item.Id] = userData?.LastPlayedDate ?? DateTime.MinValue;
				}
				catch
				{
					userDataLookup[item.Id] = DateTime.MinValue;
				}
			}
			
			return itemsList.OrderBy(item => userDataLookup[item.Id]);
		}

		private string? GetPlaylistId(Guid? userId, string playlistName)
		{
			if (userId == null) return null;
			
			var playlist = PlaylistManager.GetPlaylists(userId.Value)
				.FirstOrDefault(x => x.Name == playlistName);
			
			if (playlist != null && playlist.Id != Guid.Empty)
			{
				return playlist.Id.ToString();
			}
			
			return null;
		}


		/// <summary>
		/// Creates a placeholder BaseItemDto for empty states that links to the playlists library
		/// </summary>
		private BaseItemDto CreatePlaceholderItem(User user, string name, string overview)
		{
			// Get the playlists folder so the placeholder can link to it
			var playlistsFolder = PlaylistManager.GetPlaylistsFolder(user.Id);
			
			return new BaseItemDto
			{
				Name = name,
				Overview = overview,
				Id = playlistsFolder?.Id ?? Guid.Empty, // Use playlists folder ID to make it clickable
				Type = BaseItemKind.PlaylistsFolder,
				ServerId = user.Id.ToString("N"),
				IsFolder = true,
				ImageTags = new Dictionary<ImageType, string>(),
				BackdropImageTags = Array.Empty<string>(),
				ScreenshotImageTags = Array.Empty<string>(),
				PrimaryImageAspectRatio = 1.0,
				ParentId = playlistsFolder?.GetParent()?.Id ?? Guid.Empty,
				LockedFields = Array.Empty<MetadataField>(),
				LockData = false,
				CollectionType = CollectionType.playlists
			};
		}

		public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
		{
			string sortOrder = payload.GetEffectiveStringConfig(Section ?? string.Empty, "sortOrder", "Default");
			string sortDirection = payload.GetEffectiveStringConfig(Section ?? string.Empty, "sortDirection", "Ascending");
			string watchedItemsHandling = payload.GetEffectiveStringConfig(Section ?? string.Empty, "watchedItemsHandling", "Show");
			bool showPlaceholderWhenEmpty = payload.GetEffectiveBoolConfig(Section ?? string.Empty, "showPlaceholderWhenEmpty", false);
			int itemLimit = (int)payload.GetEffectiveDoubleConfig(Section ?? string.Empty, "itemLimit", 32.0);
			
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

			IEnumerable<Playlist> playlists = PlaylistManager.GetPlaylists(user.Id);
			Playlist? myListPlaylist = playlists.FirstOrDefault(x => x.Name == "My List");

			List<BaseItem> results = new List<BaseItem>();

			if (myListPlaylist != null)
			{
				IEnumerable<BaseItem> playlistItems = myListPlaylist.GetChildren(user, true, new InternalItemsQuery(user)
				{
					IsAiring = true
				});
				
				playlistItems = ApplySortingToItems(playlistItems, sortOrder, sortDirection, user);
				
				if (watchedItemsHandling == "Hide")
				{
					playlistItems = playlistItems.Where(item => !item.IsPlayed(user));
				}
				else if (watchedItemsHandling == "Remove")
				{
					// TODO: Implement automatic removal of watched items
				}
				
				playlistItems = playlistItems.Take(itemLimit);
				
				results.AddRange(playlistItems);
			}

			bool isPlaylistEmpty = myListPlaylist == null || !results.Any();
			
			if (isPlaylistEmpty)
			{
				if (showPlaceholderWhenEmpty)
				{
					// Show a placeholder item to indicate the section exists but is empty
					var placeholderItem = CreatePlaceholderItem(user, "Your My List is empty", "Your My List is empty");
					return new QueryResult<BaseItemDto>(new[] { placeholderItem });
				}
				else
				{
					// Hide the section when empty
					return new QueryResult<BaseItemDto>();
				}
			}

			QueryResult<BaseItemDto>? result = new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(results, dtoOptions, user));

			return result;
		}
		
		public HomeScreenSectionInfo GetInfo()
		{
			return new HomeScreenSectionInfo
			{
				Section = Section,
				DisplayText = DisplayText,
				Info = SectionInfoHelper.CreateOfficialSectionInfo(
					description: "Shows items from your 'My List' playlist, similar to Netflix's My List feature.",
					adminNotes: "Built-in section provided by the Home Screen Sections plugin."
				),
				AdditionalData = AdditionalData,
				Route = Route,
				Limit = Limit ?? 1,
				OriginalPayload = OriginalPayload,
				ViewMode = SectionViewMode.Landscape
			};
		}

		/// <summary>
		/// Get configuration options for this section
		/// </summary>
		/// <returns>Collection of configuration options</returns>
		public virtual IEnumerable<PluginConfigurationOption> GetConfigurationOptions()
		{
			return new[]
			{
				PluginConfigurationHelper.CreateDropdown(
					"sortOrder",
					"Sort By",
					"Choose how to sort playlist items on the home screen",
					new[] { "Default", "DateAdded", "Alphabetical", "PremiereDate", "Random", "CommunityRating", "RecentlyWatched" },
					new[] { "Default", "Date Added To Server", "Alphabetical", "Release Date", "Random", "Rating", "Recently Watched" },
					"Default",
					userOverridable: true,
					isAdvanced: false
				),
				PluginConfigurationHelper.CreateDropdown(
					"sortDirection",
					"Sort Direction",
					"Choose the direction for sorting",
					new[] { "Ascending", "Descending" },
					new[] { "Ascending (A-Z, Oldest First)", "Descending (Z-A, Newest First)" },
					"Descending",
					userOverridable: true,
					isAdvanced: false
				),
				PluginConfigurationHelper.CreateDropdown(
					"watchedItemsHandling",
					"Watched Items Handling",
					"Choose how to handle items that have been watched.",
					new[] { "Show", "Hide" },
					new[] { "Show", "Hide" },
					"Show",
					userOverridable: true,
					isAdvanced: false
				),
				PluginConfigurationHelper.CreateCheckbox(
					"showPlaceholderWhenEmpty",
					"Experimental: Show Placeholder When Empty",
					"Show a placeholder item when My List is empty instead of hiding the section",
					defaultValue: false,
					userOverridable: true,
					isAdvanced: true
				),
				PluginConfigurationHelper.CreateNumberBox(
					"itemLimit",
					"Item Limit",
					"Maximum number of items to display",
					defaultValue: 32,
					userOverridable: true,
					isAdvanced: true,
					minValue: 1,
					maxValue: 100,
					step: 1
				),
				PluginConfigurationHelper.CreateTextBox(
					"testTextBox",
					"Test Text Box Regex",
					"Text box",
					defaultValue: "Default text value",
					userOverridable: true,
					isAdvanced: true,
					required: true,
					minLength: 16,
					maxLength: 64,
					pattern: "^[A-Za-z0-9_-]+$"
				),
				PluginConfigurationHelper.CreateTextBox(
					"testTextBox2",
					"Test Text Box",
					"Text box no user override",
					defaultValue: "Default text value",
					userOverridable: false,
					isAdvanced: false,
					minLength: 16,
					maxLength: 64
				),
				PluginConfigurationHelper.CreateTextBox(
					"apiKey",
					"Test API Key",
					"",
					defaultValue: "",
					userOverridable: false,
					isAdvanced: false,
					required: true,
					placeholder: "No user override allowed, not shared to user",
					minLength: 32,
					maxLength: 32,
					pattern: "^[a-f0-9]{32}$",
					validationMessage: "API Key must be a 32-character hexadecimal string."
				)
			};
		}
	}
}
