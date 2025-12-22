using Jellyfin.Plugin.HomeScreenSections.Library;
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

        public override IReadOnlyList<string> PersonTypes => new[] { PersonType.Actor };
        
        public override int MinRequiredItems => 3;
        
        public override TranslationMetadata? TranslationMetadata { get; protected set; }
        
        public StarringSection(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager) : base(libraryManager, dtoService, userManager)
        {
        }

        protected override IHomeScreenSection CreateInstance(Person person)
        {
            return new StarringSection(m_libraryManager, m_dtoService, m_userManager)
            {
                AdditionalData = person.Id.ToString(),
                DisplayText = $"Starring {person.Name}",
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = person.Name,
                }
            };
        }
    }
}