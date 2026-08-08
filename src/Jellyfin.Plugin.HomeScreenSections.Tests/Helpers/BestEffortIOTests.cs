using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Helpers;

public class BestEffortIOTests : IDisposable
{
    private readonly string _tempDir;

    public BestEffortIOTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hss-io-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Write_then_read_round_trips_text()
    {
        string path = Path.Combine(_tempDir, "roundtrip.txt");

        BestEffortIO.TryWriteAllText(path, "hello");
        string? content = BestEffortIO.TryReadAllText(path);

        Assert.Equal("hello", content);
    }

    [Fact]
    public void Write_then_read_round_trips_bytes()
    {
        string path = Path.Combine(_tempDir, "roundtrip.bin");
        byte[] data = [1, 2, 3, 4];
        File.WriteAllBytes(path, data);

        byte[]? read = BestEffortIO.TryReadAllBytes(path);

        Assert.Equal(data, read);
    }

    [Fact]
    public void TryDeleteFile_removes_existing_file()
    {
        string path = Path.Combine(_tempDir, "delete-me.txt");
        File.WriteAllText(path, "data");

        BestEffortIO.TryDeleteFile(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDeleteFile_does_not_throw_for_missing_file()
    {
        Exception? captured = null;
        BestEffortIO.TryDeleteFile(Path.Combine(_tempDir, "missing.txt"), ex => captured = ex);
        Assert.Null(captured);
    }

    [Fact]
    public void TryReadAllText_returns_null_and_reports_error_for_missing_file()
    {
        Exception? captured = null;

        string? content = BestEffortIO.TryReadAllText(Path.Combine(_tempDir, "missing.txt"), ex => captured = ex);

        Assert.Null(content);
        Assert.NotNull(captured);
    }

    [Fact]
    public void TryReadAllBytes_returns_null_and_reports_error_for_missing_file()
    {
        Exception? captured = null;

        byte[]? content = BestEffortIO.TryReadAllBytes(Path.Combine(_tempDir, "missing.bin"), ex => captured = ex);

        Assert.Null(content);
        Assert.NotNull(captured);
    }

    [Fact]
    public void TryWriteAllText_reports_io_error_for_missing_directory()
    {
        // DirectoryNotFoundException derives from IOException, which the helper swallows.
        Exception? captured = null;

        BestEffortIO.TryWriteAllText(Path.Combine(_tempDir, "no-such-dir", "file.txt"), "data", ex => captured = ex);

        Assert.NotNull(captured);
        Assert.False(File.Exists(Path.Combine(_tempDir, "no-such-dir", "file.txt")));
    }

    [Fact]
    public void Read_helpers_work_without_error_callback()
    {
        Assert.Null(BestEffortIO.TryReadAllText(Path.Combine(_tempDir, "missing.txt")));
        Assert.Null(BestEffortIO.TryReadAllBytes(Path.Combine(_tempDir, "missing.bin")));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestIO.DeleteBestEffort(_tempDir);
    }
}
