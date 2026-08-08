using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons
{
    public class DirectedBySection : PersonsSectionBase
    {
        public override string? Section => "DirectedBy";
        
        public override string? DisplayText { get; set; } = "Directed by";
        
        protected override IReadOnlyList<string> PersonTypes => new [] { PersonType.Director };
        
        protected override int MinRequiredItems => 3;
        
        public override TranslationMetadata? TranslationMetadata { get; protected set; }
        
        public DirectedBySection(ILibraryManager libraryManager, IDtoService dtoService, IUserManager userManager) : base(libraryManager, dtoService, userManager)
        {
        }

        protected override IHomeScreenSection CreateInstance(Person person)
        {
            DtoOptions dtoOptions = new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio,
                    ItemFields.DisplayPreferencesId]
            };

            return new DirectedBySection(_libraryManager, _dtoService, _userManager)
            {
                AdditionalData = person.Id.ToString(),
                DisplayText = $"Directed by {person.Name}",
                OriginalPayload = _dtoService.GetBaseItemDto(person, dtoOptions),
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = person.Name,
                }
            };
        }
    }
}