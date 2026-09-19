using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons
{
    public class StarringSection : PersonsSectionBase
    {
        public override string? Section => "Starring";
        
        public override string? DisplayText { get; set; } = "Starring";

        public string? AdminDescription => "Other movies/shows featuring an actor who appears in the user's library (e.g. \"Starring Pedro Pascal\"). Each user sees a different actor, picked randomly each load.";

        protected override IReadOnlyList<string> PersonTypes => new[] { PersonType.Actor, PersonType.GuestStar };
        
        protected override int MinRequiredItems => 3;

        protected override string AdminTranslationKey => "StarringSectionName";
        
        public override TranslationMetadata? TranslationMetadata { get; protected set; }
        
        public StarringSection(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager, PerUserComputedStatsCache statsCache) : base(libraryManager, dtoService, userManager, statsCache)
        {
        }

        protected override PersonsSectionBase CreateInstance(Guid personId, string personName)
        {
            return new StarringSection(m_libraryManager, m_dtoService, m_userManager, m_statsCache)
            {
                AdditionalData = personId.ToString(),
                DisplayText = $"Starring {personName}",
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = personName,
                }
            };
        }
    }
}