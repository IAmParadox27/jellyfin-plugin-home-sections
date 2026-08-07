namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

/// <summary>
/// File-system helpers that swallow only expected IO failures (CA1031-safe).
/// </summary>
internal static class BestEffortIO
{
    public static void TryDeleteFile(string path, Action<Exception>? onError = null)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            onError?.Invoke(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            onError?.Invoke(ex);
        }
        catch (NotSupportedException ex)
        {
            onError?.Invoke(ex);
        }
    }

    public static byte[]? TryReadAllBytes(string path, Action<Exception>? onError = null)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
        catch (NotSupportedException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
    }

    public static void TryWriteAllText(string path, string contents, Action<Exception>? onError = null)
    {
        try
        {
            File.WriteAllText(path, contents);
        }
        catch (IOException ex)
        {
            onError?.Invoke(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            onError?.Invoke(ex);
        }
        catch (NotSupportedException ex)
        {
            onError?.Invoke(ex);
        }
    }

    public static string? TryReadAllText(string path, Action<Exception>? onError = null)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
        catch (NotSupportedException ex)
        {
            onError?.Invoke(ex);
            return null;
        }
    }
}
