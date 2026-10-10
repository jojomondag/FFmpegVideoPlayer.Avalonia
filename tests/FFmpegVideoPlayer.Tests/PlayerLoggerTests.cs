using System.Runtime.CompilerServices;
using System.Text.Json;
using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class PlayerLoggerTests
{
    [Fact]
    public void Fileless_logging_does_not_retain_diagnostic_payloads()
    {
        using var logger = new PlayerLogger();
        var payload = LogTemporaryPayload(logger);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(payload.IsAlive);
        GC.KeepAlive(logger);
    }

    [Fact]
    public void File_logging_preserves_payloads_clear_flush_and_disposal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"player-log-{Guid.NewGuid():N}.json");
        try
        {
            using var logger = new PlayerLogger(path);
            logger.Log("Player", "Opened", new { Width = 1920 });
            logger.Flush();
            using (var log = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var entry = log.RootElement.EnumerateArray().Last();
                Assert.Equal("Opened", entry.GetProperty("operation").GetString());
                Assert.Equal(1920, entry.GetProperty("data").GetProperty("width").GetInt32());
            }

            logger.Clear();
            using (var log = JsonDocument.Parse(File.ReadAllText(path)))
                Assert.Equal(0, log.RootElement.GetArrayLength());

            logger.Log("Player", "Stopped");
            logger.Dispose();
            var savedLog = File.ReadAllText(path);
            using (var log = JsonDocument.Parse(savedLog))
            {
                var entry = Assert.Single(log.RootElement.EnumerateArray());
                Assert.Equal("Stopped", entry.GetProperty("operation").GetString());
            }

            logger.Log("Player", "Ignored");
            logger.Clear();
            logger.Flush();
            logger.Dispose();
            Assert.Equal(savedLog, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LogTemporaryPayload(PlayerLogger logger)
    {
        var payload = new object();
        logger.Log("Player", "Frame", payload);
        return new WeakReference(payload);
    }
}
