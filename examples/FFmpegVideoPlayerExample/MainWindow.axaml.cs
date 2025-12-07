using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace FFmpegVideoPlayerExample;

public partial class MainWindow : Window
{
    private CheckBox? _showControlsCheckBox;
    private CheckBox? _transparentBgCheckBox;

    public MainWindow()
    {
        InitializeComponent();
        
        _showControlsCheckBox = this.FindControl<CheckBox>("ShowControlsCheckBox");
        _transparentBgCheckBox = this.FindControl<CheckBox>("TransparentBgCheckBox");
        
        if (_showControlsCheckBox != null)
        {
            _showControlsCheckBox.IsCheckedChanged += OnShowControlsChanged;
        }
        
        if (_transparentBgCheckBox != null)
        {
            _transparentBgCheckBox.IsCheckedChanged += OnTransparentBgChanged;
        }
    }

    private void OnShowControlsChanged(object? sender, RoutedEventArgs e)
    {
        var showControls = _showControlsCheckBox?.IsChecked ?? true;
        VideoPlayer.ShowControls = showControls;
    }

    private void OnTransparentBgChanged(object? sender, RoutedEventArgs e)
    {
        if (_transparentBgCheckBox?.IsChecked == true)
        {
            VideoPlayer.VideoBackground = Brushes.Transparent;
        }
        else
        {
            VideoPlayer.VideoBackground = Brushes.Black;
        }
    }
}
