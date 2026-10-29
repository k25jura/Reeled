using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>
/// The player's bottom bar: items are defined once (the pool in XAML) and placed into the left, centre
/// and right zones from the saved <see cref="PlayerToolbarLayout"/>. Classic puts the seek bar above
/// the buttons; Compact puts it between them on one row.
/// </summary>
public sealed partial class PlayerPage
{
    /// <summary>Every item the toolbar can show, in the order the editor lists them.</summary>
    public static readonly string[] ToolbarItemIds =
    {
        "play", "prev", "next", "moments", "repeat", "shuffle", "tracks", "queue", "volume", "fullscreen",
        "speed", "session", "snapshot", "share", "pip", "stop", "skipback", "skipforward", "frameback", "frameforward"
    };

    private readonly Dictionary<string, FrameworkElement> _toolbarItems = new();

    private void InitializeToolbar()
    {
        foreach (var child in ToolbarPool.Children.OfType<FrameworkElement>().ToList())
        {
            if (child.Tag is string id) _toolbarItems[id] = child;
        }

        TimelineSeekBar.FormatPreview = FormatSeekPreview;
        TimelineSeekBar.DragStarted += (s, e) =>
        {
            _isUserDraggingSlider = true;
            ViewModel.OnSliderDragStarted();
        };
        TimelineSeekBar.DragDelta += (s, value) =>
        {
            ViewModel.OnSliderDeltaChanged(value);
            if (_activePickTarget != TimelinePickTarget.None && TimelinePickerCurrentTimeText != null)
            {
                TimelinePickerCurrentTimeText.Text = ViewModel.FormattedCurrentTime;
            }
        };
        TimelineSeekBar.DragCompleted += (s, value) =>
        {
            _isUserDraggingSlider = false;
            ViewModel.OnSliderDragCompleted(value);
        };
        TimelineSeekBar.SizeChanged += (s, e) => RenderTimelineMarkers();

        VolumeWedgeControl.ValueChangedByUser += (s, value) => ViewModel.SetVolume((int)Math.Round(value));
        UpdateVolumeVisuals();
        UpdateShuffleVisual();
    }

    /// <summary>The bubble over the seek bar: the time, plus the moment under the pointer if any.</summary>
    private string FormatSeekPreview(double percent)
    {
        if (ViewModel.TotalTime <= TimeSpan.Zero) return string.Empty;
        var t = TimeSpan.FromSeconds(ViewModel.TotalTime.TotalSeconds * percent / 100.0);
        string time = ViewModel_FormatTime(t);

        double tolerance = Math.Max(1.0, ViewModel.TotalTime.TotalSeconds * 6.0 / Math.Max(1, TimelineSeekBar.ActualWidth));
        foreach (var bm in ViewModel.Bookmarks)
        {
            bool inside = bm.IsRange && bm.EndTimestamp.HasValue
                ? t >= bm.Timestamp && t <= bm.EndTimestamp.Value
                : Math.Abs((t - bm.Timestamp).TotalSeconds) <= tolerance;
            if (inside && !string.IsNullOrWhiteSpace(bm.Label)) return $"{bm.Label}  ·  {time}";
        }
        return time;
    }

    private static string ViewModel_FormatTime(TimeSpan t) => ViewModels.PlayerViewModel.FormatTime(t);

    public void ApplyToolbarLayout()
    {
        var layout = App.GetService<Services.ILocalStorageService>().CurrentSettings.PlayerToolbar ?? PlayerToolbarLayout.Default;
        ApplyToolbarLayout(layout);
    }

    /// <summary>Also used live by the toolbar editor while the user drags items around.</summary>
    public void ApplyToolbarLayout(PlayerToolbarLayout layout)
    {
        // Everything back to the pool first, then out to the zones in order
        foreach (var zone in new[] { ToolbarLeftZone, ToolbarCenterZone, ToolbarRightZone })
        {
            foreach (var child in zone.Children.OfType<FrameworkElement>().ToList())
            {
                zone.Children.Remove(child);
                ToolbarPool.Children.Add(child);
            }
        }

        var placed = new HashSet<string>();
        void Fill(StackPanel zone, IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (!placed.Add(id) || !_toolbarItems.TryGetValue(id, out var item)) continue;
                ToolbarPool.Children.Remove(item);
                zone.Children.Add(item);
            }
        }
        Fill(ToolbarLeftZone, layout.Left);
        Fill(ToolbarCenterZone, layout.Center);
        Fill(ToolbarRightZone, layout.Right);

        // Play must stay reachable even if a layout lost it
        if (!placed.Contains("play") && _toolbarItems.TryGetValue("play", out var play))
        {
            ToolbarPool.Children.Remove(play);
            ToolbarCenterZone.Children.Insert(0, play);
        }

        ApplyToolbarMode(layout.Mode);
        RenderTimelineMarkers();
    }

    private void ApplyToolbarMode(ToolbarLayoutMode mode)
    {
        // Detach from wherever it is (Parent isn't reliable before the page has loaded)
        BarLayoutRoot.Children.Remove(SeekRow);
        ControlsRow.Children.Remove(SeekRow);
        if (SeekRow.Parent is Panel parent) parent.Children.Remove(SeekRow);
        ControlsRow.ColumnDefinitions.Clear();

        if (mode == ToolbarLayoutMode.Compact)
        {
            // [left][centre]  time ───●─── time  [right]
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(ToolbarLeftZone, 0);
            Grid.SetColumn(ToolbarCenterZone, 1);
            Grid.SetColumn(SeekRow, 2);
            Grid.SetColumn(ToolbarRightZone, 3);
            SeekRow.Margin = new Thickness(8, 0, 8, 0);
            ControlsRow.Children.Add(SeekRow);
            ControlsRow.Margin = new Thickness(0, 4, 0, 0);
            BottomControlBar.Padding = new Thickness(12, 6, 12, 6);
        }
        else
        {
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ControlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(ToolbarLeftZone, 0);
            Grid.SetColumn(ToolbarCenterZone, 1);
            Grid.SetColumn(ToolbarRightZone, 2);
            Grid.SetRow(SeekRow, 0);
            Grid.SetColumn(SeekRow, 0);
            SeekRow.Margin = new Thickness(0);
            BarLayoutRoot.Children.Insert(0, SeekRow);
            ControlsRow.Margin = new Thickness(0);
            BottomControlBar.Padding = new Thickness(16, 4, 16, 8);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // In-player toolbar editor (right-click > Customize controls)
    // ---------------------------------------------------------------------------------------------

    private Border? _toolbarEditorOverlay;
    private Controls.ToolbarEditor? _toolbarEditor;
    private bool _isToolbarEditorOpen;

    public void OpenToolbarEditor()
    {
        var loc = Loc;
        if (_toolbarEditorOverlay == null)
        {
            _toolbarEditor = new Controls.ToolbarEditor { AllIds = ToolbarItemIds };
            _toolbarEditor.LayoutChanged += (s, layout) =>
            {
                ApplyToolbarLayout(layout);
                Storage.CurrentSettings.PlayerToolbar = layout.Clone();
                _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
            };

            var title = new TextBlock { Text = loc["Toolbar_EditorTitle"], FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var reset = new Button { Content = loc["Toolbar_Reset"], Style = (Style)Application.Current.Resources["SubtleButtonStyle"], Padding = new Thickness(12, 5, 12, 6) };
            reset.Click += (s, e) =>
            {
                var layout = PlayerToolbarLayout.Default;
                _toolbarEditor.SetLayout(layout);
                ApplyToolbarLayout(layout);
                Storage.CurrentSettings.PlayerToolbar = layout;
                _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
            };
            var done = new Button { Content = loc["Toolbar_Done"], Style = (Style)Application.Current.Resources["AccentButtonStyle"], Padding = new Thickness(18, 5, 18, 6) };
            done.Click += (s, e) => CloseToolbarEditor();

            var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(title);
            Grid.SetColumn(reset, 1);
            Grid.SetColumn(done, 2);
            header.Children.Add(reset);
            header.Children.Add(done);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(_toolbarEditor);

            _toolbarEditorOverlay = new Border
            {
                Child = content,
                MaxWidth = 760,
                Margin = new Thickness(24, 0, 24, BarClearance + 12),
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 255, 255, 255)),
                Background = (Brush)Resources["PlayerFlyoutBackdropBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                RequestedTheme = ElementTheme.Dark,
                RenderTransform = new TranslateTransform(),
                Visibility = Visibility.Collapsed,
                Opacity = 0
            };
            Helpers.VideoGlass.Wrap(_toolbarEditorOverlay, "Flyout");
            RootContainer.Children.Add(_toolbarEditorOverlay);
        }

        _toolbarEditor!.SetLayout(Storage.CurrentSettings.PlayerToolbar ?? PlayerToolbarLayout.Default);
        _isToolbarEditorOpen = true;
        _inactivityTimer.Stop();
        ViewModel.IsControlsVisible = true;
        _toolbarEditorOverlay.Visibility = Visibility.Visible;
        _videoGlass?.Track(500);
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(_toolbarEditorOverlay, "Opacity", 1, Helpers.Motion.Medium, from: 0));
        sb.Children.Add(Helpers.Motion.Spline(_toolbarEditorOverlay.RenderTransform, "Y", 0, Helpers.Motion.Long, from: 16));
        Helpers.Motion.BeginExact(sb);
    }

    private void CloseToolbarEditor()
    {
        if (!_isToolbarEditorOpen || _toolbarEditorOverlay == null) return;
        _isToolbarEditorOpen = false;
        _videoGlass?.Track(400);
        var overlay = _toolbarEditorOverlay;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(overlay, "Opacity", 0, Helpers.Motion.Short, accelerate: true));
        sb.Children.Add(Helpers.Motion.Spline(overlay.RenderTransform, "Y", 10, Helpers.Motion.Short, accelerate: true));
        sb.Completed += (s, e) => { if (!_isToolbarEditorOpen) overlay.Visibility = Visibility.Collapsed; };
        Helpers.Motion.BeginExact(sb);
        if (ViewModel.IsPlaying && !AreFlyoutsOrPopupsOpen) RestartInactivityTimer();
    }

    // ---------------------------------------------------------------------------------------------
    // Item behaviour
    // ---------------------------------------------------------------------------------------------

    private void UpdateVolumeVisuals()
    {
        if (VolumeWedgeControl == null) return;
        VolumeWedgeControl.Maximum = ViewModel.MaxVolume;
        VolumeWedgeControl.Value = ViewModel.IsMuted ? 0 : ViewModel.Volume;
        VolumeValueText.Text = ViewModel.IsMuted ? "—" : $"{ViewModel.Volume}%";
        VolumeValueText.Foreground = new SolidColorBrush(ViewModel.IsVolumeBoosted
            ? Windows.UI.Color.FromArgb(255, 255, 176, 64)
            : Windows.UI.Color.FromArgb(230, 255, 255, 255));
    }

    private void UpdateShuffleVisual()
    {
        double opacity = ViewModel.IsShuffleEnabled ? 1.0 : 0.45;
        var brush = ViewModel.IsShuffleEnabled
            ? Helpers.ThemeBrushes.Get("AccentTextFillColorPrimaryBrush", ElementTheme.Dark)
            : new SolidColorBrush(Microsoft.UI.Colors.White);
        if (ShuffleButtonIcon != null) { ShuffleButtonIcon.Opacity = opacity; ShuffleButtonIcon.Foreground = brush; }
        if (QueueShuffleIcon != null) { QueueShuffleIcon.Opacity = opacity; QueueShuffleIcon.Foreground = brush; }
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e) => ViewModel.IsShuffleEnabled = !ViewModel.IsShuffleEnabled;

    private void OnFrameBackClick(object sender, RoutedEventArgs e) => ViewModel.StepFrame(forward: false);

    private void OnFrameForwardClick(object sender, RoutedEventArgs e) => ViewModel.StepFrame(forward: true);

    private async void OnSnapshotClick(object sender, RoutedEventArgs e) => await ViewModel.TakeSnapshotAsync();

    private void OnSpeedButtonClick(object sender, RoutedEventArgs e) => OpenSessionPanel(SessionTabPlayback);

    /// <summary>Mini player: a small always-on-top window (Windows compact overlay).</summary>
    private void OnPipClick(object sender, RoutedEventArgs e) => TogglePictureInPicture();

    public void TogglePictureInPicture()
    {
        try
        {
            var appWindow = App.Window.AppWindow;
            bool isPip = appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay;
            if (!isPip && appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            {
                ToggleFullscreen();
            }
            appWindow.SetPresenter(isPip
                ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default
                : Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay);
            RefreshVideoLayout();
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("PictureInPicture", ex);
        }
    }
}
