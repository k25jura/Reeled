using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }
    private readonly DispatcherTimer _inactivityTimer;
    private bool _isUserDraggingSlider;
    private Windows.Foundation.Point _lastPointerPosition;

    private Storyboard? _controlsStoryboard;
    private bool _areControlsShowing = true;
    private Storyboard? _osdStoryboard;
    private DispatcherTimer? _osdHideTimer;
    private DispatcherTimer? _momentsDismissTimer;
    private Storyboard? _momentsPopupStoryboard;
    private string? _lastPromptClipPath;
    private int _openFlyoutsCount = 0;

    private bool AreFlyoutsOrPopupsOpen =>
        _openFlyoutsCount > 0 || (MomentsJumpPopup != null && MomentsJumpPopup.Visibility == Visibility.Visible);

    public PlayerPage()
    {
        ViewModel = App.GetService<PlayerViewModel>();
        InitializeComponent();

        int initialTimeout = 2;
        try
        {
            var storage = App.GetService<Services.ILocalStorageService>();
            if (storage?.CurrentSettings != null)
                initialTimeout = storage.CurrentSettings.AutoHideControlsSeconds;
        }
        catch { }

        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(initialTimeout > 0 ? initialTimeout : 2.0) };
        _inactivityTimer.Tick += (s, e) =>
        {
            if (ViewModel.IsPlaying && !ViewModel.IsSidebarOpen && !AreFlyoutsOrPopupsOpen)
            {
                ViewModel.IsControlsVisible = false;
            }
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.IsControlsVisible))
            {
                UpdateControlsVisibility(ViewModel.IsControlsVisible);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsSidebarOpen))
            {
                AnimateSidebar(ViewModel.IsSidebarOpen);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.StatusToast))
            {
                UpdateOsdToast(ViewModel.StatusToast);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.TotalTime))
            {
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.CurrentClip))
            {
                CheckAndShowMomentsPrompt();
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
            {
                if (!ViewModel.IsPlaying)
                {
                    ViewModel.IsControlsVisible = true;
                }
                else
                {
                    RestartInactivityTimer();
                }
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.PlaybackRate))
            {
                UpdateSpeedPresetHighlight();
            }
        };

        ViewModel.Bookmarks.CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
        };

        SizeChanged += (s, e) =>
        {
            if (Visibility == Visibility.Visible)
            {
                RefreshVideoLayout();
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            }
        };

        // Wire up pointer handlers on TimelineSlider with handledEventsToo = true to detect manual scrubbing
        TimelineSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);

        // Catch pointer movements even if handled by inner controls to wake up cursor & controls
        this.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPagePointerMoved), true);

        Loaded += (s, e) =>
        {
            Activate();
            ApplyLocalization();
            var loc = App.GetService<Services.ILocalizationService>();
            if (loc != null)
            {
                loc.LanguageChanged += OnLanguageChanged;
            }
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PreloadAndWarmupAnimations);
        };

        Unloaded += (s, e) =>
        {
            var loc = App.GetService<Services.ILocalizationService>();
            if (loc != null)
            {
                loc.LanguageChanged -= OnLanguageChanged;
            }
            Deactivate();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        try
        {
            var loc = App.GetService<Services.ILocalizationService>();
            if (loc == null) return;

            // Header Back Button
            if (BackToLibraryText != null) BackToLibraryText.Text = loc["Player_BackToLibrary"];
            if (BackToLibraryButton != null) ToolTipService.SetToolTip(BackToLibraryButton, loc["Player_Tooltip_BackToLibrary"]);

            // Playback Tooltips
            if (PlayPauseButton != null) ToolTipService.SetToolTip(PlayPauseButton, loc["Player_Tooltip_PlayPause"]);
            if (PreviousClipButton != null) ToolTipService.SetToolTip(PreviousClipButton, loc["Player_Tooltip_PreviousClip"]);
            if (StopClipButton != null) ToolTipService.SetToolTip(StopClipButton, loc["Player_Tooltip_Stop"]);
            if (NextClipButton != null) ToolTipService.SetToolTip(NextClipButton, loc["Player_Tooltip_NextClip"]);
            if (RepeatButton != null) ToolTipService.SetToolTip(RepeatButton, ViewModel.RepeatTooltip);
            if (SkipBackwardButton != null) ToolTipService.SetToolTip(SkipBackwardButton, loc["Player_Tooltip_SkipBackward"]);
            if (SkipForwardButton != null) ToolTipService.SetToolTip(SkipForwardButton, loc["Player_Tooltip_SkipForward"]);
            if (MarkMomentButton != null) ToolTipService.SetToolTip(MarkMomentButton, loc["Player_Tooltip_MarkMoment"]);
            if (MomentsFlyoutButton != null) ToolTipService.SetToolTip(MomentsFlyoutButton, loc["Player_Tooltip_TimelineBookmarks"]);
            if (ClipQueueButton != null) ToolTipService.SetToolTip(ClipQueueButton, loc["Player_Tooltip_ClipQueue"]);
            if (PlaybackSpeedDropDown != null) ToolTipService.SetToolTip(PlaybackSpeedDropDown, loc["Player_Tooltip_PlaybackSpeed"]);
            if (AudioTrackDropDown != null) ToolTipService.SetToolTip(AudioTrackDropDown, loc["Player_Tooltip_AudioTrack"]);
            if (SubtitlesDropDown != null) ToolTipService.SetToolTip(SubtitlesDropDown, loc["Player_Tooltip_Subtitles"]);
            if (MuteButton != null) ToolTipService.SetToolTip(MuteButton, loc["Player_Tooltip_MuteUnmute"]);
            if (FullscreenButton != null) ToolTipService.SetToolTip(FullscreenButton, loc["Player_Tooltip_Fullscreen"]);
            if (MomentsPromptDismissButton != null) ToolTipService.SetToolTip(MomentsPromptDismissButton, loc["Player_Dismiss"]);
            if (CloseQueueButton != null) ToolTipService.SetToolTip(CloseQueueButton, loc["Player_Tooltip_CloseQueue"]);

            // Moments Flyout
            if (MomentsFlyoutHeaderTitle != null) MomentsFlyoutHeaderTitle.Text = loc["Player_MomentsTitle"];
            if (MomentsFlyoutAddText != null) MomentsFlyoutAddText.Text = loc["Player_MomentsAdd"];
            if (MomentsFlyoutAddButton != null) ToolTipService.SetToolTip(MomentsFlyoutAddButton, loc["Player_Tooltip_MarkMomentCurrentTime"]);
            if (MomentsFlyoutEmptyTitle != null) MomentsFlyoutEmptyTitle.Text = loc["Player_NoMomentsMarked"];
            if (MomentsFlyoutEmptySubtitle != null) MomentsFlyoutEmptySubtitle.Text = loc["Player_PressBToMark"];

            // Speed Flyout
            if (SpeedFlyoutHeaderTitle != null) SpeedFlyoutHeaderTitle.Text = loc["Player_PlaybackSpeedTitle"];
            if (PresetSpeed10 != null) PresetSpeed10.Content = loc["Player_PresetNormal"];
            if (CustomSpeedLabel != null) CustomSpeedLabel.Text = loc["Player_CustomSpeed"];
            if (ResetSpeedButton != null) ResetSpeedButton.Content = loc["Player_Reset"];

            // Audio Tracks Flyout
            if (AudioTracksFlyoutHeaderTitle != null) AudioTracksFlyoutHeaderTitle.Text = loc["Player_AudioTracksTitle"];

            // Subtitles Flyout
            if (SubtitlesFlyoutHeaderTitle != null) SubtitlesFlyoutHeaderTitle.Text = loc["Player_SubtitlesTitle"];
            if (AddSubtitleFileText != null) AddSubtitleFileText.Text = loc["Player_AddSubtitleFile"];
            if (AddSubtitleFileButton != null) ToolTipService.SetToolTip(AddSubtitleFileButton, loc["Player_Tooltip_AddSubtitleFile"]);

            // Moments Jump Prompt
            if (MomentsPromptTitle != null) MomentsPromptTitle.Text = loc["Player_MomentsInClip"];

            // Queue Drawer
            if (ClipQueueHeaderTitle != null) ClipQueueHeaderTitle.Text = loc["Player_ClipQueueTitle"];
        }
        catch { }
    }

    private bool _isAnimationsWarmedUp;

    private void PreloadAndWarmupAnimations()
    {
        if (_isAnimationsWarmedUp) return;
        _isAnimationsWarmedUp = true;

        try
        {
            if (TopHeaderBar != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TopHeaderBar);
            if (BottomControlBar != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(BottomControlBar);
            if (PlaylistQueueSidebar != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PlaylistQueueSidebar);
            if (OsdToastContainer != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(OsdToastContainer);
            if (MomentsJumpPopup != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MomentsJumpPopup);
            if (RootContainer != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootContainer);
        }
        catch { }
    }

    private Storyboard? _sidebarStoryboard;

    private void AnimateSidebar(bool open, bool animate = true)
    {
        _sidebarStoryboard?.Stop();

        if (!animate)
        {
            PlaylistQueueSidebar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            PlaylistQueueSidebar.Opacity = open ? 1.0 : 0.0;
            SidebarTranslation.X = open ? 0 : 320;
            BottomControlBar.Margin = open ? new Thickness(0, 0, 320, 0) : new Thickness(0);
            MomentsJumpPopup.Margin = new Thickness(24, 0, 0, 96);
            return;
        }

        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(260);

        if (open)
        {
            PlaylistQueueSidebar.Visibility = Visibility.Visible;
            BottomControlBar.Margin = new Thickness(0, 0, 320, 0);
            MomentsJumpPopup.Margin = new Thickness(24, 0, 0, 96);

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var transAnim = new DoubleAnimation
            {
                From = SidebarTranslation.X,
                To = 0,
                Duration = duration,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(transAnim, SidebarTranslation);
            Storyboard.SetTargetProperty(transAnim, "X");

            var opAnim = new DoubleAnimation
            {
                From = PlaylistQueueSidebar.Opacity,
                To = 1.0,
                Duration = duration,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(opAnim, PlaylistQueueSidebar);
            Storyboard.SetTargetProperty(opAnim, "Opacity");

            sb.Children.Add(transAnim);
            sb.Children.Add(opAnim);

            sb.Completed += (s, e) =>
            {
                SidebarTranslation.X = 0;
                PlaylistQueueSidebar.Opacity = 1.0;
            };
        }
        else
        {
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var transAnim = new DoubleAnimation
            {
                From = SidebarTranslation.X,
                To = 320,
                Duration = duration,
                EasingFunction = easeIn
            };
            Storyboard.SetTarget(transAnim, SidebarTranslation);
            Storyboard.SetTargetProperty(transAnim, "X");

            var opAnim = new DoubleAnimation
            {
                From = PlaylistQueueSidebar.Opacity,
                To = 0.0,
                Duration = duration,
                EasingFunction = easeIn
            };
            Storyboard.SetTarget(opAnim, PlaylistQueueSidebar);
            Storyboard.SetTargetProperty(opAnim, "Opacity");

            sb.Children.Add(transAnim);
            sb.Children.Add(opAnim);

            sb.Completed += (s, e) =>
            {
                PlaylistQueueSidebar.Visibility = Visibility.Collapsed;
                SidebarTranslation.X = 320;
                PlaylistQueueSidebar.Opacity = 0.0;
                BottomControlBar.Margin = new Thickness(0);
                MomentsJumpPopup.Margin = new Thickness(16, 0, 0, 92);
            };
        }

        _sidebarStoryboard = sb;
        sb.Begin();
    }

    public void RefreshVideoLayout()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayerVideoView.InvalidateMeasure();
            PlayerVideoView.InvalidateArrange();
        });
    }

    public void Activate()
    {
        this.Focus(FocusState.Programmatic);
        ViewModel.IsControlsVisible = true;
        _areControlsShowing = false;
        UpdateControlsVisibility(true, animate: false);
        SetFullscreenLayout(ViewModel.IsFullscreen);
        AnimateSidebar(ViewModel.IsSidebarOpen, animate: false);
        RestartInactivityTimer();
        RefreshVideoLayout();
        CheckAndShowMomentsPrompt();
        DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
    }

    public void Deactivate()
    {
        _inactivityTimer.Stop();
        _osdHideTimer?.Stop();
        _osdStoryboard?.Stop();
        _controlsStoryboard?.Stop();
        _sidebarStoryboard?.Stop();
        _momentsDismissTimer?.Stop();
        _momentsPopupStoryboard?.Stop();
        _lastPromptClipPath = null;
        MomentsJumpPopup.Visibility = Visibility.Collapsed;
        MomentsJumpPopup.Opacity = 0.0;

        try
        {
            if (App.Window is MainWindow mainWindow)
            {
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true);
            }
        }
        catch { }
    }

    private void RestartInactivityTimer()
    {
        _inactivityTimer.Stop();
        try
        {
            var storage = App.GetService<Services.ILocalStorageService>();
            int seconds = storage?.CurrentSettings?.AutoHideControlsSeconds ?? 2;
            if (seconds <= 0)
            {
                // Never auto-hide controls
                return;
            }
            _inactivityTimer.Interval = TimeSpan.FromSeconds(seconds);
            _inactivityTimer.Start();
        }
        catch
        {
            _inactivityTimer.Interval = TimeSpan.FromSeconds(2.0);
            _inactivityTimer.Start();
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Activate();
    }

    private void OnVideoViewInitialized(object? sender, LibVLCSharp.Platforms.Windows.InitializedEventArgs e)
    {
        ViewModel.AttachVideoView(PlayerVideoView, e.SwapChainOptions);
    }

    private void OnPagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(this).Position;
        if (Math.Abs(currentPoint.X - _lastPointerPosition.X) < 1.0 &&
            Math.Abs(currentPoint.Y - _lastPointerPosition.Y) < 1.0)
        {
            return;
        }
        _lastPointerPosition = currentPoint;

        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.SetCursorHidden(false);
        }
        ViewModel.IsControlsVisible = true;
        if (!AreFlyoutsOrPopupsOpen)
        {
            RestartInactivityTimer();
        }
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isUserDraggingSlider = true;
        ViewModel.OnSliderDragStarted();
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            _isUserDraggingSlider = false;
            ViewModel.OnSliderDragCompleted(TimelineSlider.Value);
        }
    }

    private void OnTimelineSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            ViewModel.OnSliderDeltaChanged(e.NewValue);
        }
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int newVol = (int)Math.Round(e.NewValue);
        if (ViewModel.Volume != newVol)
        {
            ViewModel.SetVolume(newVol);
        }
    }

    private void OnSpeedItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag &&
            double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double speed))
        {
            ViewModel.ChangeSpeed(speed);
            UpdateSpeedPresetHighlight();
        }
    }

    private void OnCustomSpeedSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && (!slider.IsLoaded || slider.FocusState == FocusState.Unfocused)) return;
        if (Math.Abs(ViewModel.PlaybackRate - e.NewValue) > 0.01)
        {
            ViewModel.ChangeSpeed(Math.Round(e.NewValue, 2));
            UpdateSpeedPresetHighlight();
        }
    }

    private void OnSpeedStepDownClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AdjustSpeed(-0.05);
        UpdateSpeedPresetHighlight();
    }

    private void OnSpeedStepUpClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AdjustSpeed(0.05);
        UpdateSpeedPresetHighlight();
    }

    private void OnResetSpeedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetSpeed();
        UpdateSpeedPresetHighlight();
    }

    private void OnFlyoutOpened(object sender, object e)
    {
        _openFlyoutsCount++;
        _inactivityTimer.Stop();
        ViewModel.IsControlsVisible = true;
        if (ReferenceEquals(sender, PlaybackSpeedFlyout))
        {
            UpdateSpeedPresetHighlight();
        }
    }

    private void OnFlyoutClosed(object sender, object e)
    {
        _openFlyoutsCount = Math.Max(0, _openFlyoutsCount - 1);
        if (!AreFlyoutsOrPopupsOpen && ViewModel.IsPlaying)
        {
            RestartInactivityTimer();
        }
    }

    private void UpdateSpeedPresetHighlight()
    {
        double currentRate = Math.Round(ViewModel.PlaybackRate, 2);
        HighlightPresetButton(PresetSpeed05, 0.5, currentRate);
        HighlightPresetButton(PresetSpeed075, 0.75, currentRate);
        HighlightPresetButton(PresetSpeed10, 1.0, currentRate);
        HighlightPresetButton(PresetSpeed125, 1.25, currentRate);
        HighlightPresetButton(PresetSpeed15, 1.5, currentRate);
        HighlightPresetButton(PresetSpeed20, 2.0, currentRate);
    }

    private void HighlightPresetButton(Button? btn, double targetSpeed, double currentSpeed)
    {
        if (btn == null) return;
        bool isSelected = Math.Abs(currentSpeed - targetSpeed) < 0.01;
        if (Application.Current.Resources.TryGetValue(isSelected ? "AccentButtonStyle" : "SubtleButtonStyle", out var styleObj) && styleObj is Style style)
        {
            btn.Style = style;
        }
    }

    private void OnAudioTracksFlyoutOpening(object sender, object e)
    {
        PopulateAudioTracksList();
    }

    private void PopulateAudioTracksList()
    {
        if (AudioTracksListPanel == null) return;
        var loc = App.GetService<Services.ILocalizationService>();
        var appFont = Application.Current.Resources["AppFontFamily"] as Microsoft.UI.Xaml.Media.FontFamily;
        AudioTracksListPanel.Children.Clear();

        var tracks = ViewModel.AudioTracks;
        if (tracks.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = loc?["Player_NoAudioTracks"] ?? "No audio tracks",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(4, 8, 4, 8)
            };
            if (appFont != null) emptyText.FontFamily = appFont;
            AudioTracksListPanel.Children.Add(emptyText);
            return;
        }

        var activeIds = ViewModel.ActiveAudioTracks;
        foreach (var track in tracks)
        {
            var textBlock = new TextBlock
            {
                Text = track.DisplayName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };
            if (appFont != null) textBlock.FontFamily = appFont;

            var cb = new CheckBox
            {
                Content = textBlock,
                IsChecked = activeIds.Contains(track.Id),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2),
                Tag = track.Id
            };
            int trackId = track.Id;
            cb.Click += (s, args) =>
            {
                ViewModel.ToggleAudioTrack(trackId);
                PopulateAudioTracksList();
            };
            AudioTracksListPanel.Children.Add(cb);
        }

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        AudioTracksListPanel.Children.Add(separator);

        var disableText = new TextBlock
        {
            Text = loc?["Player_DisableAudio"] ?? "Disable Audio",
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        if (appFont != null) disableText.FontFamily = appFont;

        var disableCb = new CheckBox
        {
            Content = disableText,
            IsChecked = activeIds.Count == 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };
        disableCb.Click += (s, args) =>
        {
            ViewModel.DisableAudio();
            PopulateAudioTracksList();
        };
        AudioTracksListPanel.Children.Add(disableCb);
    }

    private void OnSubtitlesFlyoutOpening(object sender, object e)
    {
        PopulateSubtitlesList();
    }

    private void PopulateSubtitlesList()
    {
        if (SubtitlesListPanel == null) return;
        var loc = App.GetService<Services.ILocalizationService>();
        var appFont = Application.Current.Resources["AppFontFamily"] as Microsoft.UI.Xaml.Media.FontFamily;
        SubtitlesListPanel.Children.Clear();

        var tracks = ViewModel.SubtitleTracks;
        if (tracks.Count > 0)
        {
            foreach (var track in tracks)
            {
                var textBlock = new TextBlock
                {
                    Text = track.DisplayName,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                if (appFont != null) textBlock.FontFamily = appFont;

                var rb = new RadioButton
                {
                    GroupName = "SubtitlesFlyoutGroup",
                    Content = textBlock,
                    IsChecked = track.IsSelected,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 2, 4, 2),
                    Tag = track.Id
                };
                int trackId = track.Id;
                rb.Click += (s, args) =>
                {
                    ViewModel.SelectSubtitleTrack(trackId);
                    PopulateSubtitlesList();
                };
                SubtitlesListPanel.Children.Add(rb);
            }
        }
        else
        {
            var hintText = new TextBlock
            {
                Text = loc?["Player_NoSubtitlesHint"] ?? "Load an external subtitle file (.ass, .ssa, .srt, .vtt)",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 6, 4, 6)
            };
            if (appFont != null) hintText.FontFamily = appFont;
            SubtitlesListPanel.Children.Add(hintText);
        }

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        SubtitlesListPanel.Children.Add(separator);

        var disableText = new TextBlock
        {
            Text = loc?["Player_DisableSubtitles"] ?? "Disable Subtitles",
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        if (appFont != null) disableText.FontFamily = appFont;

        var disableRb = new RadioButton
        {
            GroupName = "SubtitlesFlyoutGroup",
            Content = disableText,
            IsChecked = ViewModel.SelectedSubtitleTrack == null,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };
        disableRb.Click += (s, args) =>
        {
            ViewModel.SelectSubtitleTrack(-1);
            PopulateSubtitlesList();
        };
        SubtitlesListPanel.Children.Add(disableRb);
    }

    private async void OnAddSubtitleFileClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        await ViewModel.AddSubtitleFileAsync(hwnd);
        PopulateSubtitlesList();
    }

    private void OnFullscreenButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnVideoOverlayDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ToggleFullscreen();
    }

    public void AnimateFullscreenTransition()
    {
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0.90,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, RootContainer);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void UpdateControlsVisibility(bool visible, bool animate = true)
    {
        if (!visible && AreFlyoutsOrPopupsOpen)
        {
            return;
        }

        if (_areControlsShowing == visible && animate) return;
        _areControlsShowing = visible;

        if (App.Window is MainWindow mainWindow)
        {
            if (visible)
            {
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true);
            }
            else if (ViewModel.IsPlaying)
            {
                mainWindow.SetCursorHidden(true);
                mainWindow.SetCaptionControlsVisible(false);
            }
            else
            {
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true);
            }
        }

        _controlsStoryboard?.Stop();

        if (!animate)
        {
            TopHeaderBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BottomControlBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            TopHeaderBar.Opacity = visible ? 1.0 : 0.0;
            BottomControlBar.Opacity = visible ? 1.0 : 0.0;
            return;
        }

        var sb = new Storyboard();

        if (visible)
        {
            TopHeaderBar.Visibility = Visibility.Visible;
            BottomControlBar.Visibility = Visibility.Visible;

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(200);

            var topOp = new DoubleAnimation { From = TopHeaderBar.Opacity, To = 1.0, Duration = duration, EasingFunction = easeOut };
            Storyboard.SetTarget(topOp, TopHeaderBar);
            Storyboard.SetTargetProperty(topOp, "Opacity");

            var botOp = new DoubleAnimation { From = BottomControlBar.Opacity, To = 1.0, Duration = duration, EasingFunction = easeOut };
            Storyboard.SetTarget(botOp, BottomControlBar);
            Storyboard.SetTargetProperty(botOp, "Opacity");

            sb.Children.Add(topOp);
            sb.Children.Add(botOp);

            sb.Completed += (s, e) =>
            {
                TopHeaderBar.Opacity = 1.0;
                BottomControlBar.Opacity = 1.0;
            };
        }
        else
        {
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var duration = TimeSpan.FromMilliseconds(180);

            var topOp = new DoubleAnimation { From = TopHeaderBar.Opacity, To = 0.0, Duration = duration, EasingFunction = easeIn };
            Storyboard.SetTarget(topOp, TopHeaderBar);
            Storyboard.SetTargetProperty(topOp, "Opacity");

            var botOp = new DoubleAnimation { From = BottomControlBar.Opacity, To = 0.0, Duration = duration, EasingFunction = easeIn };
            Storyboard.SetTarget(botOp, BottomControlBar);
            Storyboard.SetTargetProperty(botOp, "Opacity");

            sb.Children.Add(topOp);
            sb.Children.Add(botOp);

            sb.Completed += (s, e) =>
            {
                TopHeaderBar.Visibility = Visibility.Collapsed;
                BottomControlBar.Visibility = Visibility.Collapsed;
                TopHeaderBar.Opacity = 0.0;
                BottomControlBar.Opacity = 0.0;
            };
        }

        _controlsStoryboard = sb;
        sb.Begin();
    }

    private void SetOsdText(string message)
    {
        foreach (var child in OsdToastContainer.Children)
        {
            if (child is TextBlock tb)
            {
                tb.Text = message;
            }
        }
    }

    private void UpdateOsdToast(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        SetOsdText(message);

        // If already visible and holding/stacking keys, keep at full opacity and just reset hide timer
        if (OsdToastContainer.Visibility == Visibility.Visible && OsdToastContainer.Opacity >= 0.7 && _osdStoryboard == null)
        {
            OsdToastContainer.Opacity = 1.0;
            StartOsdHideTimer();
            return;
        }

        _osdStoryboard?.Stop();
        _osdStoryboard = null;
        _osdHideTimer?.Stop();

        OsdToastContainer.Visibility = Visibility.Visible;

        var sbIn = new Storyboard();
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opIn = new DoubleAnimation
        {
            From = OsdToastContainer.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(opIn, OsdToastContainer);
        Storyboard.SetTargetProperty(opIn, "Opacity");

        sbIn.Children.Add(opIn);
        sbIn.Completed += (s, e) =>
        {
            _osdStoryboard = null;
            OsdToastContainer.Opacity = 1.0;
            StartOsdHideTimer();
        };

        _osdStoryboard = sbIn;
        sbIn.Begin();
    }

    private void StartOsdHideTimer()
    {
        _osdHideTimer?.Stop();
        _osdHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _osdHideTimer.Tick += (s, e) =>
        {
            _osdHideTimer?.Stop();

            var sbOut = new Storyboard();
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var opOut = new DoubleAnimation
            {
                From = OsdToastContainer.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = easeIn
            };
            Storyboard.SetTarget(opOut, OsdToastContainer);
            Storyboard.SetTargetProperty(opOut, "Opacity");

            sbOut.Children.Add(opOut);
            sbOut.Completed += (s2, e2) =>
            {
                _osdStoryboard = null;
                OsdToastContainer.Visibility = Visibility.Collapsed;
                OsdToastContainer.Opacity = 0.0;
            };

            _osdStoryboard = sbOut;
            sbOut.Begin();
        };
        _osdHideTimer.Start();
    }

    public void SetFullscreenLayout(bool isFullscreen)
    {
        RefreshVideoLayout();
    }

    private void ToggleFullscreen()
    {
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.ToggleFullscreen();
        }
        else
        {
            var appWindow = App.Window.AppWindow;
            if (appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                ViewModel.IsFullscreen = false;
            }
            else
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                ViewModel.IsFullscreen = true;
            }
        }
    }

    private async void OnAddMarkerClick(object sender, RoutedEventArgs e)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var textBox = new TextBox
        {
            PlaceholderText = loc?["Player_Dialog_AddMoment_Placeholder"] ?? "E.g., Ace, Clutch, Headshot (or leave blank)",
            Text = string.Format(loc?["Player_Dialog_AddMoment_Default"] ?? "Mark at {0}", ViewModel.FormattedCurrentTime)
        };

        var dialog = new ContentDialog
        {
            Title = loc?["Player_Dialog_AddMoment_Title"] ?? "Save Moment Bookmark",
            Content = textBox,
            PrimaryButtonText = loc?["Player_Dialog_AddMoment_Save"] ?? "Save Mark",
            CloseButtonText = loc?["Player_Dialog_AddMoment_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.AddBookmarkAsync(textBox.Text);
        }
    }

    private void OnJumpToBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            ViewModel.JumpToBookmark(bm);
        }
    }

    private async void OnEditBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            var loc = App.GetService<Services.ILocalizationService>();
            var textBox = new TextBox
            {
                Text = bm.Label,
                PlaceholderText = loc?["Player_Dialog_RenameMoment_Placeholder"] ?? "Enter moment title...",
                SelectionStart = bm.Label.Length
            };

            var dialog = new ContentDialog
            {
                Title = loc?["Player_Dialog_RenameMoment_Title"] ?? "Rename Moment",
                Content = textBox,
                PrimaryButtonText = loc?["Player_Dialog_RenameMoment_Save"] ?? "Save",
                CloseButtonText = loc?["Player_Dialog_RenameMoment_Cancel"] ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.UpdateBookmarkLabelAsync(bm, textBox.Text);
            }
        }
    }

    private async void OnRemoveBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            await ViewModel.RemoveBookmarkAsync(bm);
        }
    }

    private async void CheckAndShowMomentsPrompt()
    {
        var clip = ViewModel.CurrentClip;
        if (clip == null)
        {
            HideMomentsPopup(animate: false);
            return;
        }

        if (clip.FilePath == _lastPromptClipPath)
        {
            return;
        }

        // Brief delay so media begins loading smoothly before prompt animates in
        await System.Threading.Tasks.Task.Delay(300);

        // Verify still on the same clip and page
        if (ViewModel.CurrentClip != clip) return;

        if (clip.Bookmarks.Count > 0)
        {
            _lastPromptClipPath = clip.FilePath;
            ShowMomentsPopup(clip.Bookmarks);
        }
        else
        {
            HideMomentsPopup(animate: false);
        }
    }

    private void ShowMomentsPopup(System.Collections.Generic.IReadOnlyList<ClipBookmark> bookmarks)
    {
        _momentsPopupStoryboard?.Stop();
        _momentsDismissTimer?.Stop();
        _inactivityTimer.Stop();
        ViewModel.IsControlsVisible = true;

        MomentsPopupItemsList.ItemsSource = bookmarks;
        MomentsJumpPopup.Visibility = Visibility.Visible;

        var sb = new Storyboard();
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animY = new DoubleAnimation
        {
            From = 24,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(animY, MomentsPopupTranslation);
        Storyboard.SetTargetProperty(animY, "Y");
        sb.Children.Add(animY);

        var animOp = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(animOp, MomentsJumpPopup);
        Storyboard.SetTargetProperty(animOp, "Opacity");
        sb.Children.Add(animOp);

        sb.Completed += (s, e) =>
        {
            _momentsPopupStoryboard = null;
            MomentsPopupTranslation.Y = 0;
            MomentsJumpPopup.Opacity = 1.0;
            StartMomentsDismissTimer(7000);
        };

        _momentsPopupStoryboard = sb;
        sb.Begin();
    }

    private void StartMomentsDismissTimer(int milliseconds)
    {
        _momentsDismissTimer?.Stop();
        _momentsDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        _momentsDismissTimer.Tick += (s, e) =>
        {
            _momentsDismissTimer?.Stop();
            HideMomentsPopup(animate: true);
        };
        _momentsDismissTimer.Start();
    }

    private void HideMomentsPopup(bool animate = true)
    {
        _momentsDismissTimer?.Stop();
        _momentsPopupStoryboard?.Stop();

        if (!animate)
        {
            MomentsJumpPopup.Visibility = Visibility.Collapsed;
            MomentsJumpPopup.Opacity = 0.0;
            MomentsPopupTranslation.Y = 24;
            if (!AreFlyoutsOrPopupsOpen && ViewModel.IsPlaying)
            {
                RestartInactivityTimer();
            }
            return;
        }

        var sb = new Storyboard();
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var animY = new DoubleAnimation
        {
            From = MomentsPopupTranslation.Y,
            To = 24,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(animY, MomentsPopupTranslation);
        Storyboard.SetTargetProperty(animY, "Y");
        sb.Children.Add(animY);

        var animOp = new DoubleAnimation
        {
            From = MomentsJumpPopup.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(animOp, MomentsJumpPopup);
        Storyboard.SetTargetProperty(animOp, "Opacity");
        sb.Children.Add(animOp);

        sb.Completed += (s, e) =>
        {
            _momentsPopupStoryboard = null;
            MomentsJumpPopup.Visibility = Visibility.Collapsed;
            MomentsJumpPopup.Opacity = 0.0;
            MomentsPopupTranslation.Y = 24;
            if (!AreFlyoutsOrPopupsOpen && ViewModel.IsPlaying)
            {
                RestartInactivityTimer();
            }
        };

        _momentsPopupStoryboard = sb;
        sb.Begin();
    }

    private void OnMomentsPopupPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _momentsDismissTimer?.Stop();
    }

    private void OnMomentsPopupPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (MomentsJumpPopup.Visibility == Visibility.Visible)
        {
            StartMomentsDismissTimer(4000);
        }
    }

    private void OnDismissMomentsPopupClick(object sender, RoutedEventArgs e)
    {
        HideMomentsPopup(animate: true);
    }

    private void OnPopupBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            ViewModel.JumpToBookmark(bm);
            HideMomentsPopup(animate: true);
        }
    }

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is GameClip clip)
        {
            ViewModel.SelectPlaylistClip(clip);
        }
    }

    private void OnPlaylistContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is Models.GameClip clip && clip.Thumbnail == null)
        {
            var thumbnailService = App.GetService<Services.IThumbnailService>();
            if (thumbnailService != null)
            {
                if (thumbnailService.TryGetFromMemoryCache(clip.FilePath, out var cached))
                {
                    clip.Thumbnail = cached;
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        var thumb = await thumbnailService.GetThumbnailAsync(clip.FilePath);
                        if (thumb != null)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (clip.Thumbnail == null)
                                {
                                    clip.Thumbnail = thumb;
                                }
                            });
                        }
                    });
                }
            }
        }
    }

    private void OnPlayerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(this);
        int delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta != 0)
        {
            int step = delta > 0 ? 5 : -5;
            ViewModel.SetVolume(ViewModel.Volume + step);
            e.Handled = true;
        }
    }

    private void OnPlayerPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
    }

    public void HandleKeyDown(KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShift = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Space:
                ViewModel.TogglePlayPause();
                e.Handled = true;
                break;

            case VirtualKey.Left:
                if (isShift) ViewModel.FineBackward();
                else ViewModel.SkipBackward();
                e.Handled = true;
                break;

            case VirtualKey.Right:
                if (isShift) ViewModel.FineForward();
                else ViewModel.SkipForward();
                e.Handled = true;
                break;

            case VirtualKey.Up:
                ViewModel.SetVolume(ViewModel.Volume + 5);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                ViewModel.SetVolume(ViewModel.Volume - 5);
                e.Handled = true;
                break;

            case VirtualKey.M:
                ViewModel.ToggleMute();
                e.Handled = true;
                break;

            case VirtualKey.B:
                _ = ViewModel.AddBookmarkAsync();
                e.Handled = true;
                break;

            case VirtualKey.R:
                ViewModel.ToggleRepeatMode();
                e.Handled = true;
                break;

            case VirtualKey.Q:
                ViewModel.ToggleSidebar();
                e.Handled = true;
                break;

            case VirtualKey.F:
            case VirtualKey.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (ViewModel.IsSidebarOpen)
                {
                    ViewModel.IsSidebarOpen = false;
                }
                else if (App.Window.AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                }
                else
                {
                    ViewModel.BackToHome();
                }
                e.Handled = true;
                break;
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleKeyDown(e);
    }

    private void OnTimelineSliderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderTimelineMarkers();
    }

    private void RenderTimelineMarkers()
    {
        TimelineMarkersCanvas.Children.Clear();
        if (ViewModel.CurrentClip == null || ViewModel.TotalTime <= TimeSpan.Zero) return;
        double sliderWidth = TimelineSlider.ActualWidth;
        double sliderHeight = TimelineSlider.ActualHeight;
        if (sliderWidth <= 24 || sliderHeight <= 0) return;

        const double trackPadding = 10.0;
        double usableTrackWidth = sliderWidth - (trackPadding * 2.0);
        if (usableTrackWidth <= 0) return;

        double totalSecs = ViewModel.TotalTime.TotalSeconds;
        foreach (var bm in ViewModel.Bookmarks)
        {
            double fraction = Math.Clamp(bm.Timestamp.TotalSeconds / totalSecs, 0.0, 1.0);
            double markerX = trackPadding + (fraction * usableTrackWidth) - 2.0;
            double markerY = (sliderHeight / 2.0) - 6.0;

            var marker = new Border
            {
                Width = 4,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 215, 0)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(marker, markerX);
            Canvas.SetTop(marker, markerY);
            TimelineMarkersCanvas.Children.Add(marker);
        }
    }
}
