using System;
using System.Collections.Generic;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
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

		public MyListSection(IUserManager userManager, IDtoService dtoService, IPlaylistManager playlistManager)
		{
			UserManager = userManager;
			DtoService = dtoService;
			PlaylistManager = playlistManager;
		}

	public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
	{
		var (displayName, playlistId) = GetUserPlaylistInfo(userId);
		
		// Check for custom display name configuration - this would need to be passed from the calling context
		// For now, we'll use the playlist name or default to "My List"
		string finalDisplayName = !string.IsNullOrEmpty(displayName) ? displayName : "My List";
		
		// If user has a playlist, route to the specific playlist details
		if (!string.IsNullOrEmpty(playlistId))
		{
			return new MyListSection(UserManager, DtoService, PlaylistManager)
			{
				DisplayText = finalDisplayName,
				Route = "details",
				OriginalPayload = new { Id = playlistId, Type = "Playlist" }
			};
		}
		
		// If no playlist, use simple list route to playlists library
		return new MyListSection(UserManager, DtoService, PlaylistManager)
		{
			DisplayText = finalDisplayName,
			Route = "list"
		};
	}	private (string displayName, string? playlistId) GetUserPlaylistInfo(Guid? userId)
	{
		if (userId == null) return ("My List", null);
		
		var myListPlaylist = PlaylistManager.GetPlaylists(userId.Value)
			.FirstOrDefault(x => x.Name == "My List");
		
		if (myListPlaylist != null && myListPlaylist.Id != Guid.Empty)
		{
			return (myListPlaylist.Name, myListPlaylist.Id.ToString());
		}
		
		return ("My List", null);
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
			ServerId = user.Id.ToString("N"), // Remove hyphens from GUID
			IsFolder = true, // Make it clickable to navigate to playlists library
			ImageTags = new Dictionary<ImageType, string>(),
			BackdropImageTags = Array.Empty<string>(),
			ScreenshotImageTags = Array.Empty<string>(),
			PrimaryImageAspectRatio = 1.0,
			// Set the correct parent ID for navigation
			ParentId = playlistsFolder?.GetParent()?.Id ?? Guid.Empty,
			// Make it clear this is a placeholder by setting some distinctive properties
			LockedFields = Array.Empty<MetadataField>(),
			LockData = false,
			// Ensure proper collection type for navigation
			CollectionType = CollectionType.playlists
		};
	}

	public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
	{
		bool showPlayedItems = payload.GetEffectivePluginConfiguration<bool>(Section ?? string.Empty, "supportsShowPlayedItems", true);
		bool showPlaceholderWhenEmpty = payload.GetEffectivePluginConfiguration<bool>(Section ?? string.Empty, "showPlaceholderWhenEmpty", false);
		
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
			results.AddRange(myListPlaylist.GetChildren(user, true, new InternalItemsQuery(user)
			{
				IsAiring = true
			}));
		}

		// Filter out played items if the setting is disabled
		if (!showPlayedItems)
		{
			results = results.Where(item => !item.IsPlayed(user)).ToList();
		}

		// Handle empty playlist scenarios
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
				new PluginConfigurationOption
				{
					Key = "supportsShowPlayedItems",
                    Name = "Enable Rewatching",
                    Description = "Enable showing already watched episodes (note: this option will not remove items from My List, it only affects visibility)",
					Type = PluginConfigurationType.Checkbox,
					AllowUserOverride = true,
					IsAdvanced = false,
					DefaultValue = true
				},
				new PluginConfigurationOption
				{
					Key = "showPlaceholderWhenEmpty",
					Name = "Show Placeholder When Empty",
					Description = "Show a placeholder item when My List is empty instead of hiding the section",
					Type = PluginConfigurationType.Checkbox,
					AllowUserOverride = true,
					IsAdvanced = true,
					DefaultValue = false
				},
				new PluginConfigurationOption
				{
					Key = "testTextBox",
					Name = "Test Text Box",
					Description = "This is a test text box for advanced configuration",
					Type = PluginConfigurationType.TextBox,
					AllowUserOverride = true,
					IsAdvanced = true,
					DefaultValue = "Default text value"
				},
				new PluginConfigurationOption
				{
					Key = "testNumberBox",
					Name = "Test Number Box",
					Description = "This is a test number box with min/max values",
					Type = PluginConfigurationType.NumberBox,
					AllowUserOverride = true,
					IsAdvanced = true,
					DefaultValue = 10,
					MinValue = 1,
					MaxValue = 100
				}
			};
		}
	}
}
