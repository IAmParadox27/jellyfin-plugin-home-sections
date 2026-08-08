using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Attributes;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Attributes;

public class JellyfinVersionAttributeTests
{
    private static readonly Regex s_versionRegex = new(
        @"^\d+\.\d+(\.\d+){0,2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(250));

    [Fact]
    public void GetVersion_returns_the_version_stamped_at_build_time()
    {
        string? version = JellyfinVersionAttribute.GetVersion();

        Assert.NotNull(version);
        Assert.Matches(s_versionRegex, version);
    }

    [Fact]
    public void Attribute_stores_constructor_value()
    {
        JellyfinVersionAttribute attribute = new JellyfinVersionAttribute("10.11.5");

        Assert.Equal("10.11.5", attribute.Version);
    }

    [Fact]
    public void Attribute_is_assembly_scoped()
    {
        AttributeUsageAttribute? usage = ((AttributeUsageAttribute[])typeof(JellyfinVersionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)).FirstOrDefault();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Assembly, usage!.ValidOn);
    }
}
