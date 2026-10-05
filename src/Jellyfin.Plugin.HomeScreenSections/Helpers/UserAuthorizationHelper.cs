using System.Security.Claims;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    internal static class UserAuthorizationHelper
    {
        public static bool TryGetUserId(ClaimsPrincipal principal, IUserManager userManager, Guid? requestedUserId, out Guid userId)
        {
            string? userIdString = principal.Claims.FirstOrDefault(x => x.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;

            if (!Guid.TryParse(userIdString, out userId) || userId == Guid.Empty)
            {
                return false;
            }

            if (requestedUserId.HasValue && requestedUserId.Value != userId)
            {
                return false;
            }

            return userManager.GetUserById(userId) != null;
        }
    }
}
