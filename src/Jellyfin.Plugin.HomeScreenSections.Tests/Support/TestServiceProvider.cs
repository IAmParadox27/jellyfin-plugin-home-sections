using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Minimal IServiceProvider used to let ActivatorUtilities construct the plugin's
/// built-in sections. Interfaces resolve to loose Moq mocks; the few concrete
/// dependencies sections require are constructed explicitly.
/// </summary>
internal sealed class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _resolvedCache = [];
    private readonly FakeApplicationPaths _applicationPaths;

    public TestServiceProvider(FakeApplicationPaths applicationPaths)
    {
        _applicationPaths = applicationPaths;
    }

    public T Resolve<T>() where T : class
    {
        return (T)GetService(typeof(T))!;
    }

    public object? GetService(Type serviceType)
    {
        if (_resolvedCache.TryGetValue(serviceType, out object? cached))
        {
            return cached;
        }

        object? resolved = CreateService(serviceType);
        if (resolved != null)
        {
            _resolvedCache[serviceType] = resolved;
        }

        return resolved;
    }

    private object? CreateService(Type serviceType)
    {
        if (serviceType == typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsService))
        {
            // Returning null forces ActivatorUtilities onto its classic constructor-resolution
            // path; a loose Moq mock would report every parameter as unresolvable.
            return null;
        }

        if (serviceType == typeof(UserSectionsDataCache))
        {
            return new UserSectionsDataCache();
        }

        if (serviceType == typeof(CollectionManagerProxy))
        {
            return new CollectionManagerProxy(Resolve<ICollectionManager>());
        }

        if (serviceType == typeof(ArrApiService))
        {
            return new ArrApiService(NullLogger<ArrApiService>.Instance, new HttpClient());
        }

        if (serviceType == typeof(ImageCacheService))
        {
            return new ImageCacheService(NullLogger<ImageCacheService>.Instance, _applicationPaths, new HttpClient());
        }

        if (serviceType == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            // NullLogger<T>.Instance is a static readonly field, unlike the non-generic
            // NullLogger.Instance property.
            Type nullLoggerType = typeof(NullLogger<>).MakeGenericType(serviceType.GetGenericArguments()[0]);
            return nullLoggerType
                .GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .GetValue(null);
        }

        if (serviceType.IsInterface)
        {
            Type mockType = typeof(Mock<>).MakeGenericType(serviceType);
            object mock = Activator.CreateInstance(mockType)!;
            // Mock<T> inherits a non-generic Mock.Object property, so the lookup must be
            // narrowed by return type to avoid an AmbiguousMatchException.
            return mockType.GetProperty("Object", serviceType)!.GetValue(mock);
        }

        return null;
    }
}
