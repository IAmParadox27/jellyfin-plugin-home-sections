using System.Data.Common;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    internal sealed class HomeScreenMovieVersionProjection
    {
        private readonly IDbContextFactory<JellyfinDbContext> m_contextFactory;
        private readonly string m_movieType;

        private HomeScreenMovieVersionProjection(IDbContextFactory<JellyfinDbContext> contextFactory, string movieType)
        {
            m_contextFactory = contextFactory;
            m_movieType = movieType;
        }

        internal static Func<string[], int, IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)>>? Create(
            ILibraryManager libraryManager, IServiceProvider serviceProvider)
        {
            Type managerType = libraryManager.GetType();
            // The physical certificate must use the same native store as the selected items.
            // Custom managers retain their existing query implementation.
            if (managerType.FullName != "Emby.Server.Implementations.Library.LibraryManager"
                || managerType.Assembly.GetName().Name != "Emby.Server.Implementations"
                || !ReferenceEquals(libraryManager, serviceProvider.GetService<ILibraryManager>()))
            {
                return null;
            }

            IDbContextFactory<JellyfinDbContext>? contextFactory = serviceProvider.GetService<IDbContextFactory<JellyfinDbContext>>();
            IJellyfinDatabaseProvider? databaseProvider = serviceProvider.GetService<IJellyfinDatabaseProvider>();
            IItemRepository? itemRepository = serviceProvider.GetService<IItemRepository>();
            Type? repositoryType = itemRepository?.GetType();
            IItemTypeLookup? typeLookup = serviceProvider.GetService<IItemTypeLookup>();
            if (contextFactory == null || databaseProvider == null || typeLookup == null
                || databaseProvider.GetType().FullName != "Jellyfin.Database.Providers.Sqlite.SqliteDatabaseProvider"
                || databaseProvider.GetType().Assembly.GetName().Name != "Jellyfin.Database.Providers.Sqlite"
                || !ReferenceEquals(databaseProvider.DbContextFactory, contextFactory)
                || repositoryType?.FullName != "Jellyfin.Server.Implementations.Item.BaseItemRepository"
                || repositoryType.Assembly.GetName().Name != "Jellyfin.Server.Implementations"
                || !typeLookup.BaseItemKindNames.TryGetValue(BaseItemKind.Movie, out string? movieType)
                || movieType != typeof(Movie).FullName)
            {
                return null;
            }

            return new HomeScreenMovieVersionProjection(contextFactory, movieType).GetVersions;
        }

        private IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)> GetVersions(string[] keys, int limit)
        {
            if (keys.Length != 16 || limit != 17)
            {
                return Array.Empty<(Guid, string?, DateTime?)>();
            }

            try
            {
                using JellyfinDbContext context = m_contextFactory.CreateDbContext();
                if (context.GetType() != typeof(JellyfinDbContext))
                {
                    return Array.Empty<(Guid, string?, DateTime?)>();
                }

                foreach (IDbContextOptionsExtension extension in context.GetService<IDbContextOptions>().Extensions)
                {
                    if (extension is CoreOptionsExtension coreOptions
                        && (coreOptions.Model != null || coreOptions.ReplacedServices?.Count > 0))
                    {
                        return Array.Empty<(Guid, string?, DateTime?)>();
                    }
                }

                IEntityType? itemType = context.Model.FindEntityType(typeof(BaseItemEntity));
                if (itemType == null || itemType.BaseType != null || itemType.GetDerivedTypes().Any()
                    || itemType.GetTableName() != "BaseItems" || itemType.GetViewName() != null
                    || itemType.GetSqlQuery() != null || itemType.GetQueryFilter() != null
#if NET10_0_OR_GREATER
                    || itemType.GetDeclaredQueryFilters().Any()
#endif
                   )
                {
                    return Array.Empty<(Guid, string?, DateTime?)>();
                }

                // Include every physical Movie version, even outside the user's libraries or date window.
                // These rows only certify the already-authorized selections; they are never returned as content.
                return context.BaseItems.AsNoTracking()
                    .Where(item => item.Type == m_movieType && keys.Contains(item.PresentationUniqueKey!))
                    .Take(limit)
                    .Select(item => new ValueTuple<Guid, string?, DateTime?>(item.Id, item.PresentationUniqueKey, item.PremiereDate))
                    .ToArray();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<(Guid, string?, DateTime?)>();
            }
            catch (NotSupportedException)
            {
                return Array.Empty<(Guid, string?, DateTime?)>();
            }
            catch (DbException)
            {
                return Array.Empty<(Guid, string?, DateTime?)>();
            }
        }
    }
}
