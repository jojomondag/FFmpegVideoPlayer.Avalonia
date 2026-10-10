namespace FFmpegVideoPlayer.Tests;

// Avalonia's service locator and asset URI registration are process-wide even
// when each headless session creates a separate application for its test.
[CollectionDefinition(AvaloniaUiCollection.Name, DisableParallelization = true)]
public sealed class AvaloniaUiCollection
{
    public const string Name = "Avalonia UI";
}
