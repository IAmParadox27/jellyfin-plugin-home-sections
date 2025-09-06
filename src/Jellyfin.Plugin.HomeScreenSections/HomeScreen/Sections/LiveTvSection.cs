using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
	internal class LiveTvSection : IHomeScreenSection
	{
		public string? Section => "LiveTV";

		public string? DisplayText { get; set; } = "Live TV";

		public int? Limit => 1;

		public string? Route => null;

		public string? AdditionalData { get; set; }

		public object? OriginalPayload => null;
		
		private IUserManager UserManager { get; set; }

		private IDtoService DtoService { get; set; }

		private ILiveTvManager LiveTvManager { get; set; }

		public LiveTvSection(IUserManager userManager, IDtoService dtoService, ILiveTvManager liveTvManager)
		{
			UserManager = userManager;
			DtoService = dtoService;
			LiveTvManager = liveTvManager;
		}

		public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
		{
			return this;
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

			return LiveTvManager.GetRecommendedProgramsAsync(new InternalItemsQuery(user)
			{
				Limit = 24,
				EnableTotalRecordCount = false,
				IsAiring = true,
				User = user
			}, dtoOptions, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
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
