using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

/// <summary>
/// </summary>
internal static class LibrarySectionHelper
{
    public static BaseItemDto? ResolveLibraryFolderDto(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService,
        Guid? userId,
        CollectionType collectionType,
        string? libraryId)
    {
        User? user = userManager.GetUserById(userId ?? Guid.Empty);

        Folder[] libraryFolders = libraryManager.GetUserRootFolder()
            .GetChildren(user, true)
            .OfType<Folder>()
            .Where(x => (x as ICollectionFolder)?.CollectionType == collectionType)
            .ToArray();

        Folder? folder = !string.IsNullOrEmpty(libraryId)
            ? libraryFolders.FirstOrDefault(x => string.Equals(x.Id.ToString(), libraryId, StringComparison.Ordinal))
            : null;

        folder ??= libraryFolders.FirstOrDefault();

        if (folder == null)
        {
            return null;
        }

        DtoOptions dtoOptions = new()
        {
            Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId]
        };

        return dtoService.GetBaseItemDto(folder, dtoOptions, user);
    }
}
