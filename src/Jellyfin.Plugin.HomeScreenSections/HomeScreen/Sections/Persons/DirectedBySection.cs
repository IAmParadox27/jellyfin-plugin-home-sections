using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons
{
    public class DirectedBySection : PersonsSectionBase
    {
        public override string? Section => "DirectedBy";
        
        public override string? DisplayText { get; set; } = "Directed by";

        public string? AdminDescription => "Other movies/shows from a director who appears in the user's library (e.g. \"Directed by Denis Villeneuve\"). Each user sees a different director, picked randomly each load.";
        
        protected override IReadOnlyList<string> PersonTypes => new [] { PersonType.Director };
        
        protected override int MinRequiredItems => 3;

        protected override string AdminTranslationKey => "DirectedBySectionName";
        
        public override TranslationMetadata? TranslationMetadata { get; protected set; }
        
        public DirectedBySection(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager, PerUserComputedStatsCache statsCache) : base(libraryManager, dtoService, userManager, statsCache)
        {
        }

        protected override PersonsSectionBase CreateInstance(Guid personId, string personName)
        {
            return new DirectedBySection(m_libraryManager, m_dtoService, m_userManager, m_statsCache)
            {
                AdditionalData = personId.ToString(),
                DisplayText = $"Directed by {personName}",
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = personName,
                }
            };
        }
    }
}