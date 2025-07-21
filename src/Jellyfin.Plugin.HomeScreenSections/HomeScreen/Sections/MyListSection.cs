using System;
using Jellyfin.Data.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
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
			return new MyListSection(UserManager, DtoService, PlaylistManager)
			{
				DisplayText = displayName,
				Route = !string.IsNullOrEmpty(playlistId) ? "details" : null,
				OriginalPayload = !string.IsNullOrEmpty(playlistId) 
					? new { Id = playlistId, Type = "Playlist" }
					: null
			};
		}
		
	private (string displayName, string? playlistId) GetUserPlaylistInfo(Guid? userId)
	{
		if (userId == null) return ("My List", null);
		
		var myListPlaylist = PlaylistManager.GetPlaylists(userId.Value)
			.FirstOrDefault(x => x.Name == "My List");
		
		if (myListPlaylist != null && myListPlaylist.Id != Guid.Empty)
		{
			return (myListPlaylist.Name, myListPlaylist.Id.ToString());
		}
		
		return ("My List", null);
	}		public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
		{
			bool showPlayedItems = payload.GetEffectiveShowPlayedItems(Section ?? string.Empty);
			
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
				ViewMode = SectionViewMode.Landscape,
				SupportsShowPlayedItems = true
			};
		}
	}
}
