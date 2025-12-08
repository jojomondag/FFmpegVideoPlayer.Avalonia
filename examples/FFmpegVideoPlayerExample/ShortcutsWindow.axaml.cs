using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FFmpegVideoPlayerExample;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
