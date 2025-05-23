﻿using System.Reflection;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen
{
    public class CollectionManagerProxy
    {
        private readonly ICollectionManager m_collectionManager;

        public CollectionManagerProxy(ICollectionManager collectionManager)
        {
            m_collectionManager = collectionManager;
        }

        public IEnumerable<BoxSet> GetCollections(User user)
        {
            return m_collectionManager.GetType()
                .GetMethod("GetCollections", BindingFlags.Instance | BindingFlags.NonPublic)?
                .Invoke(m_collectionManager, new object?[]
                {
                    user
                }) as IEnumerable<BoxSet> ?? Enumerable.Empty<BoxSet>();
        }
    }
}