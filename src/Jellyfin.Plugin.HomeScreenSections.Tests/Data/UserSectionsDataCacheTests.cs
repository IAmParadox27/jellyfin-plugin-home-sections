using Jellyfin.Plugin.HomeScreenSections.Data;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Data;

public class UserSectionsDataCacheTests
{
    [Theory]
    [InlineData(5, 5, 5, true)]
    [InlineData(1, 5, 1, true)]
    [InlineData(1, 5, 5, true)]
    [InlineData(1, 5, 0, false)]
    [InlineData(1, 5, 6, false)]
    [InlineData(-5, -1, -3, true)]
    public void IntRange_contains_value_within_bounds_inclusive(int start, int end, int value, bool expected)
    {
        IntRange range = new IntRange { Start = start, End = end };
        Assert.Equal(expected, range.Contains(value));
    }

    [Fact]
    public void IntRange_equals_compares_start_and_end()
    {
        IntRange first = new IntRange { Start = 1, End = 5 };
        IntRange same = new IntRange { Start = 1, End = 5 };
        IntRange differentEnd = new IntRange { Start = 1, End = 6 };
        IntRange differentStart = new IntRange { Start = 2, End = 5 };

        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.False(first.Equals(differentEnd));
        Assert.False(first.Equals(differentStart));
        Assert.False(first.Equals((IntRange?)null));
        Assert.False(first.Equals(new object()));
    }

    [Fact]
    public void IntRange_equal_ranges_share_hash_code()
    {
        IntRange first = new IntRange { Start = 3, End = 9 };
        IntRange same = new IntRange { Start = 3, End = 9 };
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void IntRange_deduplicates_in_hash_sets()
    {
        HashSet<IntRange> ranges =
        [
            new IntRange { Start = 1, End = 2 },
            new IntRange { Start = 1, End = 2 },
            new IntRange { Start = 3, End = 4 }
        ];

        Assert.Equal(2, ranges.Count);
    }

    [Fact]
    public void Clear_removes_every_page()
    {
        UserSectionsDataCache cache = new UserSectionsDataCache();
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        cache.Cache[Guid.NewGuid()] = MakeData(userA);
        cache.Cache[Guid.NewGuid()] = MakeData(userB);

        cache.Clear();

        Assert.Empty(cache.Cache);
    }

    [Fact]
    public void ClearForUser_removes_only_that_users_pages()
    {
        UserSectionsDataCache cache = new UserSectionsDataCache();
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid pageA1 = Guid.NewGuid();
        Guid pageA2 = Guid.NewGuid();
        Guid pageB1 = Guid.NewGuid();
        cache.Cache[pageA1] = MakeData(userA);
        cache.Cache[pageA2] = MakeData(userA);
        cache.Cache[pageB1] = MakeData(userB);

        cache.ClearForUser(userA);

        Assert.Single(cache.Cache);
        Assert.True(cache.Cache.ContainsKey(pageB1));
        Assert.False(cache.Cache.ContainsKey(pageA1));
        Assert.False(cache.Cache.ContainsKey(pageA2));
    }

    [Fact]
    public void ClearForUser_with_unknown_user_keeps_all_pages()
    {
        UserSectionsDataCache cache = new UserSectionsDataCache();
        cache.Cache[Guid.NewGuid()] = MakeData(Guid.NewGuid());

        cache.ClearForUser(Guid.NewGuid());

        Assert.Single(cache.Cache);
    }

    [Fact]
    public void UserSectionsData_has_empty_collections_by_default()
    {
        UserSectionsData data = MakeData(Guid.NewGuid());

        Assert.Empty(data.OrderedSections);
        Assert.Empty(data.OrderIndicesWithoutSections);
        Assert.Empty(data.SectionsInProgress);
        Assert.Null(data.LastAccessed);
    }

    private static UserSectionsData MakeData(Guid userId)
    {
        return new UserSectionsData
        {
            UserId = userId,
            MaxOrderIndex = 10
        };
    }
}
