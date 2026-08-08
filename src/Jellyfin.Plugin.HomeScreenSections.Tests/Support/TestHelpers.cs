using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Best-effort recursive delete for the per-test temp sandboxes. Cleanup must never fail
/// a test, so IO/permission errors are swallowed.
/// </summary>
public static class TestIO
{
    public static void DeleteBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Shared stub for the repeated IDtoService.GetBaseItemDtos setup that maps each item to a
/// BaseItemDto carrying its Id and Name.
/// </summary>
public static class TestDtos
{
    public static void StubPassthrough(Mock<IDtoService> dtoService)
    {
        dtoService
            .Setup(service => service.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> list, DtoOptions options, User user, BaseItem owner) =>
                list.Select(item => new BaseItemDto { Id = item.Id, Name = item.Name }).ToArray());
    }
}
