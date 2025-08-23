using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;

public class TopTenSection : IHomeScreenSection
{
    private enum TopTenType
    {
        Movies,
        Shows
    }
    private readonly IUserManager m_userManager;
    private readonly ICollectionManager m_collectionManager;
    private readonly IDtoService m_dtoService;
    public string? Section => "TopTen";
    public string? DisplayText { get; set; } = "Top Ten";
    public int? Limit => 2;
    public string? Route => null;
    public string? AdditionalData { get; set; } = null;
    public object? OriginalPayload => null;
    
    private TopTenType Type { get; set; }

    public TopTenSection(IUserManager userManager,
        ICollectionManager collectionManager,
        IDtoService dtoService)
    {
        m_userManager = userManager;
        m_collectionManager = collectionManager;
        m_dtoService = dtoService;
    }
    
    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        DtoOptions dtoOptions = new DtoOptions
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

        User user = m_userManager.GetUserById(payload.UserId)!;
        
        // TODO: Add config variable for collection name.
        BoxSet? collection = m_collectionManager.GetCollections(user)
            .FirstOrDefault(x => x.Name == "Top Ten");

        TopTenType type = Enum.Parse<TopTenType>(payload.AdditionalData ?? "Movies");
        
        IReadOnlyList<BaseItem> items =  collection?.GetChildren(user, true) ?? new List<BaseItem>();
        items = items.Where(x => (x is Movie && type == TopTenType.Movies) || (x is Series && type == TopTenType.Shows)).ToList();
        
        items = items.Take(Math.Min(items.Count, 10)).ToList();
        
        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(items, dtoOptions, user));
    }

    public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
    {
        if (otherInstances == null ||
            !otherInstances.Any(x => x is TopTenSection { Type: TopTenType.Movies }))
        {
            return new TopTenSection(m_userManager, m_collectionManager, m_dtoService)
            {
                AdditionalData = TopTenType.Movies.ToString(),
                DisplayText = $"{DisplayText} Movies",
                Type = TopTenType.Movies,
            };
        }
        else
        {
            if (otherInstances!.Any(x => x is TopTenSection { Type: TopTenType.Shows }))
            {
                throw new Exception("Ahhhh");
            }
            
            return new TopTenSection(m_userManager, m_collectionManager, m_dtoService)
            {
                AdditionalData = TopTenType.Shows.ToString(),
                DisplayText = $"{DisplayText} Shows",
                Type = TopTenType.Shows,
            };
        }
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
            ContainerClass = "top-ten",
            DisplayTitleText = false,
            ShowDetailsMenu = false,
            ViewMode = SectionViewMode.Portrait,
            AllowViewModeChange = false
        };
    }
}