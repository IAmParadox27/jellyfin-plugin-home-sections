using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Dictionary-backed IQueryCollection, avoids Moq's awkward out-parameter setups
/// for TryGetValue(string, out StringValues).
/// </summary>
public sealed class FakeQueryCollection : Dictionary<string, StringValues>, IQueryCollection
{
    ICollection<string> IQueryCollection.Keys => Keys;
}
