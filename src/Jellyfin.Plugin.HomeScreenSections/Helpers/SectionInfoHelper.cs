using System;
using Jellyfin.Plugin.HomeScreenSections.Model;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    /// <summary>
    /// Helper for creating section info for built-in sections
    /// </summary>
    public static class SectionInfoHelper
    {
        // String constants for supported platforms
        public const string GitHub = "github";
        public const string GitLab = "gitlab";
        public const string Bitbucket = "bitbucket";
        
        // Official feature request base URL
        public const string OfficialFeatureRequestBaseUrl = "https://features.iamparadox.dev";
        
        /// <summary>
        /// Creates section info for official IAmParadox built-in sections
        /// </summary>
        /// <param name="description">Description of the section</param>
        /// <param name="adminNotes">Optional admin notes for the section</param>
        /// <returns>SectionInfo with official repository URLs and feature request URL</returns>
        public static SectionInfo CreateOfficialSectionInfo(
            string description,
            string? adminNotes = null)
        {
            // All official sections use the home-screen-sections tag
            const string iamParadoxFeatureRequestTag = "home-screen-sections";
            
            // Build the feature request URL from the tag
            string? featureRequestUrl = BuildFeatureRequestUrl(iamParadoxFeatureRequestTag);
            
            // Build VCS URLs for official IAmParadox repository
            var (repositoryUrl, issuesUrl) = BuildVcsUrls(GitHub, "IAmParadox27", "jellyfin-plugin-home-sections", true);
            
            return new SectionInfo
            {
                Description = description,
                AdminNotes = adminNotes,
                VersionControl = new VersionControlInfo
                {
                    Platform = GitHub,
                    Username = "IAmParadox27",
                    Repository = "jellyfin-plugin-home-sections",
                    IncludeIssuesLink = true,
                    RepositoryUrl = repositoryUrl,
                    IssuesUrl = issuesUrl
                },
                FeatureRequestUrl = featureRequestUrl
            };
        }
        
        /// <summary>
        /// Builds VCS URLs from platform, username, and repository
        /// </summary>
        /// <param name="platform">The VCS platform</param>
        /// <param name="username">The username</param>
        /// <param name="repository">The repository name</param>
        /// <param name="includeIssuesLink">Whether to build the issues URL</param>
        /// <returns>A tuple of (repositoryUrl, issuesUrl)</returns>
        public static (string? repositoryUrl, string? issuesUrl) BuildVcsUrls(string platform, string username, string repository, bool includeIssuesLink)
        {
            if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(repository))
            {
                return (null, null);
            }
            
            string? baseUrl = GetVcsBaseUrl(platform);
            if (baseUrl == null)
            {
                return (null, null);
            }
            
            string repositoryUrl = $"{baseUrl}/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(repository)}";
            string? issuesUrl = null;
            
            if (includeIssuesLink)
            {
                string issuesPath = GetVcsIssuesPath(platform);
                issuesUrl = repositoryUrl + issuesPath;
            }
            
            return (repositoryUrl, issuesUrl);
        }
        
        /// <summary>
        /// Gets the base URL for a VCS platform
        /// </summary>
        /// <param name="platform">The platform name</param>
        /// <returns>The base URL or null if unknown</returns>
        private static string? GetVcsBaseUrl(string platform)
        {
            return platform.ToLowerInvariant() switch
            {
                "github" => "https://github.com",
                "gitlab" => "https://gitlab.com",
                "bitbucket" => "https://bitbucket.org",
                _ => null
            };
        }
        
        /// <summary>
        /// Gets the issues path for a VCS platform
        /// </summary>
        /// <param name="platform">The platform name</param>
        /// <returns>The issues path</returns>
        private static string GetVcsIssuesPath(string platform)
        {
            return platform.ToLowerInvariant() switch
            {
                "github" => "/issues",
                "gitlab" => "/-/issues",
                "bitbucket" => "/issues",
                _ => "/issues"
            };
        }
        
        /// <summary>
        /// Builds the feature request URL from a tag
        /// </summary>
        /// <param name="tag">The tag to use. null = no URL, empty = base URL, value = tagged URL</param>
        /// <returns>The built URL or null</returns>
        public static string? BuildFeatureRequestUrl(string? tag)
        {
            if (tag == null)
            {
                return null; // No feature request link
            }
            
            if (string.IsNullOrEmpty(tag))
            {
                return OfficialFeatureRequestBaseUrl;
            }
            
            return $"{OfficialFeatureRequestBaseUrl}?tags={Uri.EscapeDataString(tag)}";
        }
    }
}
