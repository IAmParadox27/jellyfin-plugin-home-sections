using Jellyfin.Plugin.HomeScreenSections.Library;
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
        
        public override IReadOnlyList<string> PersonTypes => new [] { PersonType.Director };
        
        public override int MinRequiredItems => 1;
        
        public override TranslationMetadata? TranslationMetadata { get; protected set; }
        
        public DirectedBySection(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager) : base(libraryManager, dtoService, userManager)
        {
        }

        protected override IHomeScreenSection CreateInstance(Person person)
        {
            return new DirectedBySection(m_libraryManager, m_dtoService, m_userManager)
            {
                AdditionalData = person.Id.ToString(),
                DisplayText = $"Directed by {person.Name}",
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = person.Name,
                }
            };
        }
    }
}