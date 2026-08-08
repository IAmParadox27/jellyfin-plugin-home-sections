using Jellyfin.Extensions;
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
    private readonly IUserManager _userManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IDtoService _dtoService;
    public string? Section => "TopTen";
    public string? DisplayText { get; set; } = "Top Ten";
    public int? Limit => 2;
    public string? Route => null;
    public string? AdditionalData { get; set; }
    public object? OriginalPayload => null;
    
    private TopTenType Type { get; set; }

    public TopTenSection(IUserManager userManager,
        ICollectionManager collectionManager,
        IDtoService dtoService)
    {
        _userManager = userManager;
        _collectionManager = collectionManager;
        _dtoService = dtoService;
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

        User user = _userManager.GetUserById(payload.UserId)!;
        
        // NOTE: Add config variable for collection name.
        BoxSet? collection = _collectionManager.GetCollections(user)
            .FirstOrDefault(x => string.Equals(x.Name, "Top Ten", StringComparison.Ordinal));

        TopTenType type = Enum.Parse<TopTenType>(payload.AdditionalData ?? "Movies");
        
        List<BaseItem> items = (collection?.GetChildren(user, true, null) ?? Enumerable.Empty<BaseItem>())
            .Where(x => (x is Movie && type == TopTenType.Movies) || (x is Series && type == TopTenType.Shows))
            .Take(10)
            .ToList();
        
        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(items, dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        List<TopTenSection> sections = [];
        
        sections.Add(new TopTenSection(_userManager, _collectionManager, _dtoService)
        {
            AdditionalData = TopTenType.Movies.ToString(),
            DisplayText = $"{DisplayText} Movies",
            Type = TopTenType.Movies,
        });
        
        sections.Add(new TopTenSection(_userManager, _collectionManager, _dtoService)
        {
            AdditionalData = TopTenType.Shows.ToString(),
            DisplayText = $"{DisplayText} Shows",
            Type = TopTenType.Shows,
        });

        sections.Shuffle();

        // Return up to the instance count.
        for (int i = 0; i < instanceCount && i < sections.Count; i++)
        {
            yield return sections[i];
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