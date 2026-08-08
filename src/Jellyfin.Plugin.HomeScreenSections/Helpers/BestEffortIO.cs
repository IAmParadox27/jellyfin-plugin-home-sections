namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

/// <summary>
/// File-system helpers that swallow only expected IO failures (CA1031-safe).
/// </summary>
internal static class BestEffortIO
{
    public static void TryDeleteFile(string path, Action<Exception>? onError = null)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { onError?.Invoke(ex); }
    }

    public static byte[]? TryReadAllBytes(string path, Action<Exception>? onError = null)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { onError?.Invoke(ex); return null; }
    }

    public static void TryWriteAllText(string path, string contents, Action<Exception>? onError = null)
    {
        try { File.WriteAllText(path, contents); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { onError?.Invoke(ex); }
    }

    public static string? TryReadAllText(string path, Action<Exception>? onError = null)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { onError?.Invoke(ex); return null; }
    }
}
