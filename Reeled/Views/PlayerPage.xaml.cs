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
    private bool _isMarkRangeMode;
    private TimeSpan _markStartTime;
    private TimeSpan _markEndTime;
    private string _selectedMarkColor = "#FFD700";
    private Storyboard? _activeMomentBadgeStoryboard;
    private Storyboard? _markModeSwitchStoryboard;
    private Guid? _dismissedMomentId;
    private bool _colorButtonsInitialized;
    private enum TimelinePickTarget { None, Point, RangeStart, RangeEnd }
    private TimelinePickTarget _activePickTarget = TimelinePickTarget.None;
    private ClipBookmark? _editingBookmark;
    private DispatcherTimer? _smoothTimelineTimer;

    private bool AreFlyoutsOrPopupsOpen =>
        _openFlyoutsCount > 0 ||
        (MomentsJumpPopup != null && MomentsJumpPopup.Visibility == Visibility.Visible) ||
        (MarkMomentFlyout != null && MarkMomentFlyout.IsOpen);

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
                if (PlaylistQueueListView != null && ViewModel.CurrentClip != null)
                {
                    PlaylistQueueListView.SelectedItem = ViewModel.CurrentClip;
                    if (PlaylistQueueSidebar.Visibility == Visibility.Visible)
                    {
                        PlaylistQueueListView.ScrollIntoView(ViewModel.CurrentClip);
                    }
                }
                CheckAndShowMomentsPrompt();
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.ActiveBookmark))
            {
                UpdateActiveMomentBadge(ViewModel.ActiveBookmark);
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
            else if (e.PropertyName == nameof(PlayerViewModel.Volume) ||
                     e.PropertyName == nameof(PlayerViewModel.IsMuted) ||
                     e.PropertyName == nameof(PlayerViewModel.VolumeGlyph))
            {
                AnimateVolumeIconPop();
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
                UpdateResponsiveLayout(e.NewSize.Width);
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
            HookButtonPulses();
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
        foreach (var bm in ViewModel.Bookmarks)
        {
            bm.RefreshLocalization();
        }
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

            // Mark Moment Flyout
            if (MarkMomentFlyoutTitle != null) MarkMomentFlyoutTitle.Text = _editingBookmark != null ? (loc["Player_EditMomentTitle"] ?? "Edit Moment") : loc["Player_MarkMoment_Title"];
            if (MarkPointModeButton != null) MarkPointModeButton.Content = loc["Player_MarkMoment_Point"];
            if (MarkRangeModeButton != null) MarkRangeModeButton.Content = loc["Player_MarkMoment_Range"];
            if (MarkPointSetCurrentButton != null) MarkPointSetCurrentButton.Content = loc["Player_MarkMoment_SetCurrent"];
            if (MarkPointChooseButton != null) MarkPointChooseButton.Content = loc["Player_ChooseOnTimeline"];
            if (MarkRangeStartLabel != null) MarkRangeStartLabel.Text = loc["Player_MarkMoment_StartTime"];
            if (MarkRangeSetStartButton != null) MarkRangeSetStartButton.Content = loc["Player_MarkMoment_SetCurrent"];
            if (MarkRangeChooseStartButton != null) MarkRangeChooseStartButton.Content = loc["Player_ChooseOnTimeline"];
            if (MarkRangeEndLabel != null) MarkRangeEndLabel.Text = loc["Player_MarkMoment_EndTime"];
            if (MarkRangeSetEndButton != null) MarkRangeSetEndButton.Content = loc["Player_MarkMoment_SetCurrent"];
            if (MarkRangeChooseEndButton != null) MarkRangeChooseEndButton.Content = loc["Player_ChooseOnTimeline"];
            if (MarkMomentLabelBox != null) MarkMomentLabelBox.PlaceholderText = loc["Player_MarkMoment_TitlePlaceholder"];
            if (MarkMomentColorLabel != null) MarkMomentColorLabel.Text = loc["Player_MarkMoment_Color"];
            if (CustomColorPickerButton != null) ToolTipService.SetToolTip(CustomColorPickerButton, loc["Player_CustomColorTooltip"]);
            if (MarkMomentSaveButton != null) MarkMomentSaveButton.Content = _editingBookmark != null ? (loc["Player_EditMomentSave"] ?? "Save Changes") : loc["Player_MarkMoment_Save"];

            // Moments Jump Prompt & Active Moment Prompt
            if (MomentsPromptTitle != null) MomentsPromptTitle.Text = loc["Player_MomentsInClip"];
            if (DismissActiveMomentButton != null) ToolTipService.SetToolTip(DismissActiveMomentButton, loc["Player_ActiveMoment_DismissTooltip"] ?? loc["Player_Dismiss"]);

            // Timeline Picker Prompt
            if (TimelinePickerPromptText != null) TimelinePickerPromptText.Text = loc["Player_PickingTimelinePrompt"];
            if (TimelinePickerConfirmButton != null) TimelinePickerConfirmButton.Content = loc["Player_ConfirmTimelineChoice"];
            if (TimelinePickerCancelButton != null) TimelinePickerCancelButton.Content = loc["Player_CancelTimelineChoice"];

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
            BottomControlBarQueueSpacer.Width = open ? 320 : 0;
            if (open && PlaylistQueueListView != null && ViewModel.CurrentClip != null)
            {
                PlaylistQueueListView.SelectedItem = ViewModel.CurrentClip;
                PlaylistQueueListView.ScrollIntoView(ViewModel.CurrentClip);
            }
            return;
        }

        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(260);

        if (open)
        {
            PlaylistQueueSidebar.Visibility = Visibility.Visible;
            if (PlaylistQueueListView != null && ViewModel.CurrentClip != null)
            {
                PlaylistQueueListView.SelectedItem = ViewModel.CurrentClip;
                PlaylistQueueListView.ScrollIntoView(ViewModel.CurrentClip);
            }

            var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

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

            var spacerAnim = new DoubleAnimation
            {
                From = BottomControlBarQueueSpacer.Width,
                To = 320,
                Duration = duration,
                EasingFunction = easeOut,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(spacerAnim, BottomControlBarQueueSpacer);
            Storyboard.SetTargetProperty(spacerAnim, "Width");

            sb.Children.Add(transAnim);
            sb.Children.Add(opAnim);
            sb.Children.Add(spacerAnim);

            sb.Completed += (s, e) =>
            {
                _sidebarStoryboard = null;
                SidebarTranslation.X = 0;
                PlaylistQueueSidebar.Opacity = 1.0;
                BottomControlBarQueueSpacer.Width = 320;
            };
        }
        else
        {
            var easeIn = new QuarticEase { EasingMode = EasingMode.EaseIn };

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

            var spacerAnim = new DoubleAnimation
            {
                From = BottomControlBarQueueSpacer.Width,
                To = 0,
                Duration = duration,
                EasingFunction = easeIn,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(spacerAnim, BottomControlBarQueueSpacer);
            Storyboard.SetTargetProperty(spacerAnim, "Width");

            sb.Children.Add(transAnim);
            sb.Children.Add(opAnim);
            sb.Children.Add(spacerAnim);

            sb.Completed += (s, e) =>
            {
                _sidebarStoryboard = null;
                PlaylistQueueSidebar.Visibility = Visibility.Collapsed;
                SidebarTranslation.X = 320;
                PlaylistQueueSidebar.Opacity = 0.0;
                BottomControlBarQueueSpacer.Width = 0;
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

    private void UpdateResponsiveLayout(double width)
    {
        if (VolumeControlGroup != null)
        {
            VolumeControlGroup.Visibility = width < 760 ? Visibility.Collapsed : Visibility.Visible;
        }
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
        UpdateResponsiveLayout(ActualWidth);
        CheckAndShowMomentsPrompt();
        DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
        UpdateBackdropBlur();

        if (_smoothTimelineTimer == null)
        {
            _smoothTimelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _smoothTimelineTimer.Tick += (s, e) =>
            {
                if (ViewModel.IsPlaying && !_isUserDraggingSlider && ViewModel.TotalTime > TimeSpan.Zero && ViewModel.LastPositionTimeTicks > 0)
                {
                    double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - ViewModel.LastPositionTimeTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                    if (elapsed >= 0 && elapsed < 0.6)
                    {
                        double currentSec = ViewModel.AnchorSeconds + (elapsed * ViewModel.PlaybackRate);
                        double pct = (currentSec / ViewModel.TotalTime.TotalSeconds) * 100.0;
                        TimelineSlider.Value = Math.Clamp(pct, 0.0, 100.0);
                    }
                }
            };
        }
        _smoothTimelineTimer.Start();
    }

    public void Deactivate()
    {
        _smoothTimelineTimer?.Stop();
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
        UpdateCursorHiddenState(false);

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        UpdateCursorHiddenState(false);
    }

    private void UpdateCursorHiddenState(bool hide)
    {
        var settingsService = App.GetService<Services.ILocalStorageService>();
        bool allowAutoHide = settingsService?.CurrentSettings?.AutoHideCursor ?? true;

        if (hide && allowAutoHide)
        {
            var blank = Helpers.CursorHelper.GetBlankInputCursor();
            this.ProtectedCursor = blank;
            Helpers.CursorHelper.SetElementCursor(PlayerVideoView, blank);
            Helpers.CursorHelper.SetElementCursor(MediaOverlayButton, blank);
        }
        else
        {
            this.ProtectedCursor = null;
            Helpers.CursorHelper.SetElementCursor(PlayerVideoView, null);
            Helpers.CursorHelper.SetElementCursor(MediaOverlayButton, null);
        }
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

        UpdateCursorHiddenState(false);
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

        if (_activePickTarget != TimelinePickTarget.None && TimelinePickerCurrentTimeText != null)
        {
            TimelinePickerCurrentTimeText.Text = ViewModel.FormattedCurrentTime;
        }
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int newVol = (int)Math.Round(e.NewValue / 5.0) * 5;
        newVol = Math.Clamp(newVol, 0, 100);
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
        if (_activePickTarget == TimelinePickTarget.None && ReferenceEquals(sender, MarkMomentFlyout))
        {
            _editingBookmark = null;
        }
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
            bool styleChanged = btn.Style != style;
            btn.Style = style;
            if (isSelected && styleChanged)
            {
                AnimateElementClickPulse(btn, 0.92);
            }
        }
    }

    private bool _buttonPulsesHooked;

    private void HookButtonPulses()
    {
        if (_buttonPulsesHooked) return;
        _buttonPulsesHooked = true;

        Button?[] buttons =
        {
            PlayPauseButton,
            PreviousClipButton,
            NextClipButton,
            StopClipButton,
            RepeatButton,
            SkipBackwardButton,
            SkipForwardButton,
            MarkMomentButton,
            MomentsFlyoutButton,
            ClipQueueButton,
            BackToLibraryButton,
            CloseQueueButton,
            FullscreenButton,
            AddSubtitleFileButton,
            MomentsFlyoutAddButton,
            PresetSpeed05,
            PresetSpeed075,
            PresetSpeed10,
            PresetSpeed125,
            PresetSpeed15,
            PresetSpeed20,
            ResetSpeedButton,
            MarkPointModeButton,
            MarkRangeModeButton,
            CustomColorPickerButton,
            PlaybackSpeedDropDown,
            AudioTrackDropDown,
            SubtitlesDropDown
        };

        foreach (var btn in buttons)
        {
            if (btn != null)
            {
                btn.Click += (s, e) =>
                {
                    if (s is UIElement uie)
                    {
                        AnimateElementClickPulse(uie, 0.92);
                    }
                };
            }
        }
    }

    private static void AnimateElementClickPulse(UIElement? element, double targetScale = 0.92)
    {
        if (element == null) return;
        if (element is DropDownButton ddb && ddb.Content is UIElement c)
        {
            element = c;
        }
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        ScaleTransform scale;
        if (element.RenderTransform is ScaleTransform st)
        {
            scale = st;
        }
        else if (element.RenderTransform is TransformGroup tg)
        {
            var existing = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (existing != null)
            {
                scale = existing;
            }
            else
            {
                scale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                tg.Children.Add(scale);
            }
        }
        else
        {
            scale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            element.RenderTransform = scale;
        }

        var sb = new Storyboard();
        var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimationUsingKeyFrames();
        animX.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            Value = scale.ScaleX,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animX.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            Value = targetScale,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)),
            EasingFunction = easeOut
        });
        animX.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            EasingFunction = easeOut
        });

        var animY = new DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            Value = scale.ScaleY,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animY.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            Value = targetScale,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)),
            EasingFunction = easeOut
        });
        animY.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            EasingFunction = easeOut
        });

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        Storyboard.SetTarget(animX, scale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, scale);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Begin();
    }


    private void AnimateVolumeIconPop()
    {
        if (MuteButtonIcon != null)
        {
            AnimateElementClickPulse(MuteButtonIcon, 0.94);
        }
        else if (MuteButton != null)
        {
            AnimateElementClickPulse(MuteButton, 0.94);
        }
    }

    private void UpdateBackdropBlur()
    {
        try
        {
            var storage = App.GetService<Services.ILocalStorageService>();
            bool enableBlur = storage?.CurrentSettings?.EnableBackdropBlur ?? true;
            if (Resources.TryGetValue("PlayerFlyoutBackdropBrush", out var brushObj) && brushObj is AcrylicBrush acrylic)
            {
                acrylic.AlwaysUseFallback = !enableBlur;
                acrylic.FallbackColor = enableBlur
                    ? Windows.UI.Color.FromArgb(196, 22, 22, 22)
                    : Windows.UI.Color.FromArgb(255, 22, 22, 22);
            }
        }
        catch { }
    }

    private void AnimateMarkModeSwitch(FrameworkElement toShow, TranslateTransform? toShowTranslation, FrameworkElement toHide, bool isRangeMode)
    {
        _markModeSwitchStoryboard?.Stop();
        toHide.Visibility = Visibility.Collapsed;
        toShow.Visibility = Visibility.Visible;
        toShow.Opacity = 0.0;
        if (toShowTranslation != null)
        {
            toShowTranslation.Y = isRangeMode ? 6 : -6;
        }

        var sb = new Storyboard();
        var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new QuarticEase { EasingMode = EasingMode.EaseInOut };

        if (MarkModeContainer != null)
        {
            double targetHeight = isRangeMode ? 146.0 : 36.0;
            double fromHeight = MarkModeContainer.ActualHeight > 0 ? MarkModeContainer.ActualHeight : (isRangeMode ? 36.0 : 146.0);
            var heightAnim = new DoubleAnimation
            {
                From = fromHeight,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(isRangeMode ? 200 : 180),
                EasingFunction = isRangeMode ? easeOut : easeInOut,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(heightAnim, MarkModeContainer);
            Storyboard.SetTargetProperty(heightAnim, "Height");
            sb.Children.Add(heightAnim);
        }

        var opAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(opAnim, toShow);
        Storyboard.SetTargetProperty(opAnim, "Opacity");
        sb.Children.Add(opAnim);

        if (toShowTranslation != null)
        {
            var yAnim = new DoubleAnimation
            {
                From = isRangeMode ? 6.0 : -6.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(yAnim, toShowTranslation);
            Storyboard.SetTargetProperty(yAnim, "Y");
            sb.Children.Add(yAnim);
        }

        sb.Completed += (s, e) =>
        {
            _markModeSwitchStoryboard = null;
            toShow.Opacity = 1.0;
            if (toShowTranslation != null)
            {
                toShowTranslation.Y = 0;
            }
            if (MarkModeContainer != null)
            {
                MarkModeContainer.Height = isRangeMode ? 146.0 : 36.0;
            }
        };
        _markModeSwitchStoryboard = sb;
        sb.Begin();
    }

    private readonly Dictionary<int, CheckBox> _audioTrackCheckBoxes = new();
    private CheckBox? _disableAudioCheckBox;

    private void OnAudioTracksFlyoutOpening(object sender, object e)
    {
        ViewModel.RefreshAudioTracks();
        _audioTrackCheckBoxes.Clear();
        _disableAudioCheckBox = null;
        AudioTracksListPanel.Children.Clear();
        PopulateAudioTracksList();
    }

    private void PopulateAudioTracksList()
    {
        if (AudioTracksListPanel == null) return;
        var loc = App.GetService<Services.ILocalizationService>();
        var appFont = Application.Current.Resources["AppFontFamily"] as Microsoft.UI.Xaml.Media.FontFamily;

        var tracks = ViewModel.AudioTracks;
        if (tracks.Count == 0)
        {
            _audioTrackCheckBoxes.Clear();
            _disableAudioCheckBox = null;
            AudioTracksListPanel.Children.Clear();

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

        bool needsRebuild = _audioTrackCheckBoxes.Count != tracks.Count ||
                            tracks.Any(t => !_audioTrackCheckBoxes.TryGetValue(t.Id, out var cb) || cb.Content is not TextBlock tb || tb.Text != t.DisplayName);
        if (needsRebuild)
        {
            _audioTrackCheckBoxes.Clear();
            _disableAudioCheckBox = null;
            AudioTracksListPanel.Children.Clear();

            var activeIds = ViewModel.ActiveAudioTracks;

            foreach (var track in tracks)
            {
                var textBlock = new TextBlock
                {
                    Text = track.DisplayName,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                if (appFont != null) textBlock.FontFamily = appFont;

                var cb = new CheckBox
                {
                    Content = textBlock,
                    IsChecked = activeIds.Contains(track.Id),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = track.Id
                };

                int trackId = track.Id;
                cb.Click += (s, args) =>
                {
                    ViewModel.ToggleAudioTrack(trackId);
                    UpdateAudioTrackRowsState();
                };

                AudioTracksListPanel.Children.Add(cb);
                _audioTrackCheckBoxes[track.Id] = cb;
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
                Margin = new Thickness(0, -2, 0, 0)
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
                UpdateAudioTrackRowsState();
            };
            _disableAudioCheckBox = disableCb;
            AudioTracksListPanel.Children.Add(disableCb);
        }
        else
        {
            UpdateAudioTrackRowsState();
        }
    }

    private void UpdateAudioTrackRowsState()
    {
        var activeIds = ViewModel.ActiveAudioTracks;

        foreach (var (trackId, cb) in _audioTrackCheckBoxes)
        {
            bool isChecked = activeIds.Contains(trackId);
            if (cb.IsChecked != isChecked)
            {
                cb.IsChecked = isChecked;
            }
        }

        if (_disableAudioCheckBox != null)
        {
            bool disableChecked = activeIds.Count == 0;
            if (_disableAudioCheckBox.IsChecked != disableChecked)
            {
                _disableAudioCheckBox.IsChecked = disableChecked;
            }
        }
    }

    private void OnElementClipSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UIElement uie)
        {
            uie.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(-20, 0, Math.Max(0, e.NewSize.Width + 40), Math.Max(0, e.NewSize.Height))
            };
        }
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
                    Margin = new Thickness(0, -2, 0, 0)
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
            Margin = new Thickness(0, -2, 0, 0)
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
                UpdateCursorHiddenState(false);
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true);
            }
            else if (ViewModel.IsPlaying)
            {
                UpdateCursorHiddenState(true);
                mainWindow.SetCursorHidden(true);
                mainWindow.SetCaptionControlsVisible(false);
            }
            else
            {
                UpdateCursorHiddenState(false);
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true);
            }
        }
        else
        {
            UpdateCursorHiddenState(!visible && ViewModel.IsPlaying);
        }

        _controlsStoryboard?.Stop();

        if (!animate)
        {
            TopHeaderBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BottomControlBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            TopHeaderBar.Opacity = visible ? 1.0 : 0.0;
            BottomControlBar.Opacity = visible ? 1.0 : 0.0;
            if (ActiveMomentBadgeTranslation != null)
            {
                ActiveMomentBadgeTranslation.Y = visible ? -64.0 : 0.0;
            }
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

            if (ActiveMomentBadgeTranslation != null)
            {
                var badgeAnim = new DoubleAnimation
                {
                    From = ActiveMomentBadgeTranslation.Y,
                    To = -64.0,
                    Duration = duration,
                    EasingFunction = easeOut
                };
                Storyboard.SetTarget(badgeAnim, ActiveMomentBadgeTranslation);
                Storyboard.SetTargetProperty(badgeAnim, "Y");
                sb.Children.Add(badgeAnim);
            }

            sb.Completed += (s, e) =>
            {
                TopHeaderBar.Opacity = 1.0;
                BottomControlBar.Opacity = 1.0;
                if (ActiveMomentBadgeTranslation != null)
                {
                    ActiveMomentBadgeTranslation.Y = -64.0;
                }
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

            if (ActiveMomentBadgeTranslation != null)
            {
                var badgeAnim = new DoubleAnimation
                {
                    From = ActiveMomentBadgeTranslation.Y,
                    To = 0.0,
                    Duration = duration,
                    EasingFunction = easeIn
                };
                Storyboard.SetTarget(badgeAnim, ActiveMomentBadgeTranslation);
                Storyboard.SetTargetProperty(badgeAnim, "Y");
                sb.Children.Add(badgeAnim);
            }

            sb.Completed += (s, e) =>
            {
                TopHeaderBar.Visibility = Visibility.Collapsed;
                BottomControlBar.Visibility = Visibility.Collapsed;
                TopHeaderBar.Opacity = 0.0;
                BottomControlBar.Opacity = 0.0;
                if (ActiveMomentBadgeTranslation != null)
                {
                    ActiveMomentBadgeTranslation.Y = 0.0;
                }
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

    private void OnAddMarkerClick(object sender, RoutedEventArgs e)
    {
        if (MarkMomentButton?.Flyout != null)
        {
            MarkMomentButton.Flyout.ShowAt(MarkMomentButton);
        }
    }

    private void OnMarkMomentFlyoutOpened(object sender, object e)
    {
        OnFlyoutOpened(sender, e);

        if (_activePickTarget == TimelinePickTarget.None && _editingBookmark == null)
        {
            var loc = App.GetService<Services.ILocalizationService>();
            if (MarkMomentFlyoutTitle != null) MarkMomentFlyoutTitle.Text = loc?["Player_MarkMoment_Title"] ?? "Mark Moment";
            if (MarkMomentSaveButton != null) MarkMomentSaveButton.Content = loc?["Player_MarkMoment_Save"] ?? "Save Moment";

            _markStartTime = ViewModel.CurrentTime;
            _markEndTime = ViewModel.CurrentTime + TimeSpan.FromSeconds(5);
            if (ViewModel.TotalTime > TimeSpan.Zero && _markEndTime > ViewModel.TotalTime)
            {
                _markEndTime = ViewModel.TotalTime;
            }

            if (MarkPointTimeBox != null) MarkPointTimeBox.Text = FormatTimestamp(_markStartTime);
            if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(_markStartTime);
            if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
            if (MarkMomentLabelBox != null) MarkMomentLabelBox.Text = string.Empty;
        }

        if (MarkModeContainer != null)
        {
            MarkModeContainer.Height = _isMarkRangeMode ? 146.0 : 36.0;
        }
        if (MarkPointPanel != null)
        {
            MarkPointPanel.Visibility = _isMarkRangeMode ? Visibility.Collapsed : Visibility.Visible;
            MarkPointPanel.Opacity = 1.0;
        }
        if (MarkPointTranslation != null) MarkPointTranslation.Y = 0;
        if (MarkRangePanel != null)
        {
            MarkRangePanel.Visibility = _isMarkRangeMode ? Visibility.Visible : Visibility.Collapsed;
            MarkRangePanel.Opacity = 1.0;
        }
        if (MarkRangeTranslation != null) MarkRangeTranslation.Y = 0;

        UpdateMarkMomentPlaceholder();
        UpdateMarkColorButtons();
    }

    private static string FormatTimestamp(TimeSpan t) =>
        t.Hours > 0 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");

    private void UpdateMarkMomentPlaceholder()
    {
        if (MarkMomentLabelBox == null) return;
        var loc = App.GetService<Services.ILocalizationService>();
        string placeholder = loc?["Player_MarkMoment_TitlePlaceholder"] ?? "Title or note (optional)";
        MarkMomentLabelBox.PlaceholderText = placeholder;
    }

    private void OnMarkModePointClick(object sender, RoutedEventArgs e)
    {
        _isMarkRangeMode = false;
        MarkPointModeButton.Style = null;
        MarkRangeModeButton.Style = Application.Current.Resources["SubtleButtonStyle"] as Style;
        AnimateMarkModeSwitch(MarkPointPanel, MarkPointTranslation, MarkRangePanel, isRangeMode: false);
        UpdateMarkMomentPlaceholder();
    }

    private void OnMarkModeRangeClick(object sender, RoutedEventArgs e)
    {
        _isMarkRangeMode = true;
        MarkRangeModeButton.Style = null;
        MarkPointModeButton.Style = Application.Current.Resources["SubtleButtonStyle"] as Style;
        AnimateMarkModeSwitch(MarkRangePanel, MarkRangeTranslation, MarkPointPanel, isRangeMode: true);
        UpdateMarkMomentPlaceholder();
    }

    private void OnMarkPointSetCurrentClick(object sender, RoutedEventArgs e)
    {
        _markStartTime = ViewModel.CurrentTime;
        if (MarkPointTimeBox != null) MarkPointTimeBox.Text = FormatTimestamp(_markStartTime);
        UpdateMarkMomentPlaceholder();
    }

    private void OnMarkPointChooseClick(object sender, RoutedEventArgs e)
    {
        StartTimelinePicking(TimelinePickTarget.Point);
    }

    private void OnMarkRangeSetStartClick(object sender, RoutedEventArgs e)
    {
        _markStartTime = ViewModel.CurrentTime;
        if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(_markStartTime);
        if (_markEndTime <= _markStartTime)
        {
            _markEndTime = _markStartTime + TimeSpan.FromSeconds(5);
            if (ViewModel.TotalTime > TimeSpan.Zero && _markEndTime > ViewModel.TotalTime)
            {
                _markEndTime = ViewModel.TotalTime;
            }
            if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
        }
        UpdateMarkMomentPlaceholder();
    }

    private void OnMarkRangeChooseStartClick(object sender, RoutedEventArgs e)
    {
        StartTimelinePicking(TimelinePickTarget.RangeStart);
    }

    private void OnMarkRangeSetEndClick(object sender, RoutedEventArgs e)
    {
        _markEndTime = ViewModel.CurrentTime;
        if (_markEndTime < _markStartTime)
        {
            _markStartTime = _markEndTime - TimeSpan.FromSeconds(5);
            if (_markStartTime < TimeSpan.Zero) _markStartTime = TimeSpan.Zero;
            if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(_markStartTime);
        }
        if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
        UpdateMarkMomentPlaceholder();
    }

    private void OnMarkRangeChooseEndClick(object sender, RoutedEventArgs e)
    {
        StartTimelinePicking(TimelinePickTarget.RangeEnd);
    }

    private void StartTimelinePicking(TimelinePickTarget target)
    {
        _activePickTarget = target;
        MarkMomentFlyout?.Hide();

        var loc = App.GetService<Services.ILocalizationService>();
        if (TimelinePickerPromptText != null)
            TimelinePickerPromptText.Text = loc?["Player_PickingTimelinePrompt"] ?? "Scrub the timeline to the desired moment, then click Set";
        if (TimelinePickerCurrentTimeText != null)
            TimelinePickerCurrentTimeText.Text = ViewModel.FormattedCurrentTime;
        if (TimelinePickerConfirmButton != null)
            TimelinePickerConfirmButton.Content = loc?["Player_ConfirmTimelineChoice"] ?? "Set";
        if (TimelinePickerCancelButton != null)
            TimelinePickerCancelButton.Content = loc?["Player_CancelTimelineChoice"] ?? "Cancel";

        if (TimelinePickerPromptBadge != null)
        {
            TimelinePickerPromptBadge.Visibility = Visibility.Visible;
            TimelinePickerPromptBadge.Opacity = 1.0;
        }
    }

    private void OnConfirmTimelinePickerClick(object sender, RoutedEventArgs e)
    {
        var chosen = ViewModel.CurrentTime;
        if (_activePickTarget == TimelinePickTarget.Point)
        {
            _markStartTime = chosen;
            if (MarkPointTimeBox != null) MarkPointTimeBox.Text = FormatTimestamp(chosen);
        }
        else if (_activePickTarget == TimelinePickTarget.RangeStart)
        {
            _markStartTime = chosen;
            if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(chosen);
            if (_markEndTime <= _markStartTime)
            {
                _markEndTime = _markStartTime + TimeSpan.FromSeconds(5);
                if (ViewModel.TotalTime > TimeSpan.Zero && _markEndTime > ViewModel.TotalTime) _markEndTime = ViewModel.TotalTime;
                if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
            }
        }
        else if (_activePickTarget == TimelinePickTarget.RangeEnd)
        {
            _markEndTime = chosen;
            if (_markEndTime < _markStartTime)
            {
                _markStartTime = _markEndTime - TimeSpan.FromSeconds(5);
                if (_markStartTime < TimeSpan.Zero) _markStartTime = TimeSpan.Zero;
                if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(_markStartTime);
            }
            if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(chosen);
        }

        _activePickTarget = TimelinePickTarget.None;
        if (TimelinePickerPromptBadge != null)
        {
            TimelinePickerPromptBadge.Visibility = Visibility.Collapsed;
            TimelinePickerPromptBadge.Opacity = 0.0;
        }

        MarkMomentFlyout?.ShowAt(MarkMomentButton);
    }

    private void OnCancelTimelinePickerClick(object sender, RoutedEventArgs e)
    {
        _activePickTarget = TimelinePickTarget.None;
        if (TimelinePickerPromptBadge != null)
        {
            TimelinePickerPromptBadge.Visibility = Visibility.Collapsed;
            TimelinePickerPromptBadge.Opacity = 0.0;
        }

        MarkMomentFlyout?.ShowAt(MarkMomentButton);
    }

    private void OnQuickRangeDurationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int secs))
        {
            _markEndTime = _markStartTime + TimeSpan.FromSeconds(secs);
            if (ViewModel.TotalTime > TimeSpan.Zero && _markEndTime > ViewModel.TotalTime)
            {
                _markEndTime = ViewModel.TotalTime;
            }
            if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
            UpdateMarkMomentPlaceholder();
        }
    }

    private void UpdateMarkColorButtons()
    {
        if (MarkMomentColorPanel == null) return;
        if (!_colorButtonsInitialized)
        {
            _colorButtonsInitialized = true;
            foreach (var child in MarkMomentColorPanel.Children)
            {
                if (child is Button colorBtn && colorBtn.Tag is string hex)
                {
                    if (hex == "Custom") continue;
                    colorBtn.Click += (s, e) =>
                    {
                        _selectedMarkColor = hex;
                        UpdateMarkColorSelectionHighlight();
                    };
                }
            }
        }
        UpdateMarkColorSelectionHighlight();
    }

    private void UpdateMarkColorSelectionHighlight()
    {
        if (MarkMomentColorPanel == null) return;
        bool matchedPreset = false;
        foreach (var child in MarkMomentColorPanel.Children)
        {
            if (child is Button colorBtn && colorBtn.Tag is string hex)
            {
                if (hex == "Custom") continue;
                bool isSelected = string.Equals(hex, _selectedMarkColor, StringComparison.OrdinalIgnoreCase);
                if (isSelected) matchedPreset = true;
                colorBtn.BorderThickness = new Thickness(isSelected ? 2 : 1);
                colorBtn.BorderBrush = isSelected
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));
            }
        }
        if (CustomColorPickerButton != null)
        {
            bool isCustomSelected = !matchedPreset && !string.IsNullOrEmpty(_selectedMarkColor);
            CustomColorPickerButton.BorderThickness = new Thickness(isCustomSelected ? 2 : 1);
            CustomColorPickerButton.BorderBrush = isCustomSelected
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));
            if (isCustomSelected)
            {
                var parsed = ParseColor(_selectedMarkColor);
                CustomColorPickerButton.Background = new SolidColorBrush(parsed);
                if (CustomColorHexInput != null && CustomColorHexInput.Text != _selectedMarkColor)
                {
                    CustomColorHexInput.Text = _selectedMarkColor;
                }
                if (CustomColorPickerControl != null)
                {
                    CustomColorPickerControl.Color = parsed;
                }
                if (CustomColorPickerPipetteIcon != null)
                {
                    double luminance = (0.299 * parsed.R + 0.587 * parsed.G + 0.114 * parsed.B) / 255.0;
                    CustomColorPickerPipetteIcon.Foreground = luminance > 0.55
                        ? new SolidColorBrush(Microsoft.UI.Colors.Black)
                        : new SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }
    }

    private void OnCustomColorPickerColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        var col = args.NewColor;
        _selectedMarkColor = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        if (CustomColorHexInput != null && CustomColorHexInput.Text != _selectedMarkColor)
        {
            CustomColorHexInput.Text = _selectedMarkColor;
        }
        if (CustomColorPickerButton != null)
        {
            CustomColorPickerButton.Background = new SolidColorBrush(col);
            CustomColorPickerButton.BorderThickness = new Thickness(2);
            CustomColorPickerButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        if (CustomColorPickerPipetteIcon != null)
        {
            double luminance = (0.299 * col.R + 0.587 * col.G + 0.114 * col.B) / 255.0;
            CustomColorPickerPipetteIcon.Foreground = luminance > 0.55
                ? new SolidColorBrush(Microsoft.UI.Colors.Black)
                : new SolidColorBrush(Microsoft.UI.Colors.White);
        }
    }

    private void OnCustomColorHexInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (CustomColorHexInput == null) return;
        string text = CustomColorHexInput.Text.Trim();
        if (!text.StartsWith('#') && text.Length == 6)
        {
            text = "#" + text;
        }
        if (text.Length == 7 && text.StartsWith('#'))
        {
            try
            {
                var col = ParseColor(text);
                _selectedMarkColor = text;
                if (CustomColorPickerControl != null && CustomColorPickerControl.Color != col)
                {
                    CustomColorPickerControl.Color = col;
                }
                if (CustomColorPickerButton != null)
                {
                    CustomColorPickerButton.Background = new SolidColorBrush(col);
                    CustomColorPickerButton.BorderThickness = new Thickness(2);
                    CustomColorPickerButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
                if (CustomColorPickerPipetteIcon != null)
                {
                    double luminance = (0.299 * col.R + 0.587 * col.G + 0.114 * col.B) / 255.0;
                    CustomColorPickerPipetteIcon.Foreground = luminance > 0.55
                        ? new SolidColorBrush(Microsoft.UI.Colors.Black)
                        : new SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch { }
        }
    }

    private async void OnSaveMarkMomentClick(object sender, RoutedEventArgs e)
    {
        string? label = string.IsNullOrWhiteSpace(MarkMomentLabelBox.Text) ? null : MarkMomentLabelBox.Text.Trim();
        if (_editingBookmark != null)
        {
            TimeSpan start = _markStartTime;
            TimeSpan? end = _isMarkRangeMode ? _markEndTime : null;
            await ViewModel.UpdateBookmarkDetailsAsync(_editingBookmark, start, end, label ?? string.Empty, _selectedMarkColor);
            RenderTimelineMarkers();
            _editingBookmark = null;
        }
        else
        {
            if (_isMarkRangeMode)
            {
                await ViewModel.AddBookmarkWithDetailsAsync(label, _markEndTime, _selectedMarkColor);
            }
            else
            {
                await ViewModel.AddBookmarkWithDetailsAsync(label, null, _selectedMarkColor);
            }
        }
        if (MarkMomentFlyout != null)
        {
            MarkMomentFlyout.Hide();
        }
    }

    private void OnDismissActiveMomentClick(object sender, RoutedEventArgs e)
    {
        var currentBm = ViewModel.ActiveBookmark;
        if (currentBm != null)
        {
            _dismissedMomentId = currentBm.Id;
        }
        HideActiveMomentBadge();
    }

    private void HideActiveMomentBadge()
    {
        if (ActiveMomentPromptBadge == null || ActiveMomentPromptBadge.Visibility != Visibility.Visible) return;
        _activeMomentBadgeStoryboard?.Stop();
        var sb = new Storyboard();
        var fadeOut = new DoubleAnimation
        {
            From = ActiveMomentPromptBadge.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeOut, ActiveMomentPromptBadge);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        sb.Children.Add(fadeOut);
        sb.Completed += (s, e) =>
        {
            _activeMomentBadgeStoryboard = null;
            ActiveMomentPromptBadge.Visibility = Visibility.Collapsed;
            ActiveMomentPromptBadge.Opacity = 0.0;
        };
        _activeMomentBadgeStoryboard = sb;
        sb.Begin();
    }

    private void UpdateActiveMomentBadge(ClipBookmark? bookmark)
    {
        if (ActiveMomentPromptBadge == null) return;

        if (bookmark == null)
        {
            _dismissedMomentId = null;
            HideActiveMomentBadge();
            return;
        }

        if (bookmark.Id == _dismissedMomentId)
        {
            return;
        }

        var color = ParseColor(bookmark.ColorHex);
        ActiveMomentDot.Fill = new SolidColorBrush(color);
        ActiveMomentText.Text = bookmark.Label;
        ActiveMomentRange.Text = bookmark.FormattedRange;

        if (ActiveMomentPromptBadge.Visibility != Visibility.Visible || ActiveMomentPromptBadge.Opacity < 0.9)
        {
            _activeMomentBadgeStoryboard?.Stop();
            ActiveMomentPromptBadge.Visibility = Visibility.Visible;
            if (ActiveMomentBadgeTranslation != null)
            {
                ActiveMomentBadgeTranslation.Y = _areControlsShowing ? -64.0 : 0.0;
            }
            var sb = new Storyboard();
            var fadeIn = new DoubleAnimation
            {
                From = ActiveMomentPromptBadge.Opacity,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, ActiveMomentPromptBadge);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);
            sb.Completed += (s, e) =>
            {
                _activeMomentBadgeStoryboard = null;
                ActiveMomentPromptBadge.Opacity = 1.0;
            };
            _activeMomentBadgeStoryboard = sb;
            sb.Begin();
        }
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Windows.UI.Color.FromArgb(255, 255, 215, 0);
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }
        return Windows.UI.Color.FromArgb(255, 255, 215, 0);
    }

    private void OnJumpToBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            ViewModel.JumpToBookmark(bm);
        }
    }

    private void OnEditBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipBookmark bm)
        {
            _editingBookmark = bm;

            if (MomentsFlyoutButton?.Flyout != null && MomentsFlyoutButton.Flyout.IsOpen)
            {
                MomentsFlyoutButton.Flyout.Hide();
            }

            _markStartTime = bm.Timestamp;
            _selectedMarkColor = string.IsNullOrEmpty(bm.ColorHex) ? "#FFD700" : bm.ColorHex;

            if (bm.IsRange && bm.EndTimestamp.HasValue && bm.EndTimestamp.Value > bm.Timestamp)
            {
                _isMarkRangeMode = true;
                _markEndTime = bm.EndTimestamp.Value;
                if (MarkRangeModeButton != null) MarkRangeModeButton.Style = null;
                if (MarkPointModeButton != null) MarkPointModeButton.Style = Application.Current.Resources["SubtleButtonStyle"] as Style;
                if (MarkModeContainer != null) MarkModeContainer.Height = 146.0;
                if (MarkPointPanel != null) MarkPointPanel.Visibility = Visibility.Collapsed;
                if (MarkRangePanel != null)
                {
                    MarkRangePanel.Visibility = Visibility.Visible;
                    MarkRangePanel.Opacity = 1.0;
                    if (MarkRangeTranslation != null) MarkRangeTranslation.Y = 0;
                }
                if (MarkRangeStartTimeBox != null) MarkRangeStartTimeBox.Text = FormatTimestamp(_markStartTime);
                if (MarkRangeEndTimeBox != null) MarkRangeEndTimeBox.Text = FormatTimestamp(_markEndTime);
            }
            else
            {
                _isMarkRangeMode = false;
                _markEndTime = bm.Timestamp + TimeSpan.FromSeconds(5);
                if (MarkPointModeButton != null) MarkPointModeButton.Style = null;
                if (MarkRangeModeButton != null) MarkRangeModeButton.Style = Application.Current.Resources["SubtleButtonStyle"] as Style;
                if (MarkModeContainer != null) MarkModeContainer.Height = 36.0;
                if (MarkRangePanel != null) MarkRangePanel.Visibility = Visibility.Collapsed;
                if (MarkPointPanel != null)
                {
                    MarkPointPanel.Visibility = Visibility.Visible;
                    MarkPointPanel.Opacity = 1.0;
                    if (MarkPointTranslation != null) MarkPointTranslation.Y = 0;
                }
                if (MarkPointTimeBox != null) MarkPointTimeBox.Text = FormatTimestamp(_markStartTime);
            }

            if (MarkMomentLabelBox != null)
            {
                MarkMomentLabelBox.Text = bm.Label ?? string.Empty;
            }

            var loc = App.GetService<Services.ILocalizationService>();
            if (MarkMomentFlyoutTitle != null)
            {
                MarkMomentFlyoutTitle.Text = loc?["Player_EditMomentTitle"] ?? "Edit Moment";
            }
            if (MarkMomentSaveButton != null)
            {
                MarkMomentSaveButton.Content = loc?["Player_EditMomentSave"] ?? "Save Changes";
            }

            UpdateMarkColorButtons();

            if (MarkMomentFlyout != null && MarkMomentButton != null)
            {
                MarkMomentFlyout.ShowAt(MarkMomentButton);
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
        var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation
        {
            From = -30,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(animX, MomentsPopupTranslation);
        Storyboard.SetTargetProperty(animX, "X");
        sb.Children.Add(animX);

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
            MomentsPopupTranslation.X = 0;
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
            MomentsPopupTranslation.X = -30;
            if (!AreFlyoutsOrPopupsOpen && ViewModel.IsPlaying)
            {
                RestartInactivityTimer();
            }
            return;
        }

        var sb = new Storyboard();
        var easeIn = new QuarticEase { EasingMode = EasingMode.EaseIn };

        var animX = new DoubleAnimation
        {
            From = MomentsPopupTranslation.X,
            To = -30,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(animX, MomentsPopupTranslation);
        Storyboard.SetTargetProperty(animX, "X");
        sb.Children.Add(animX);

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
            MomentsPopupTranslation.X = -30;
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
            if (clip != ViewModel.CurrentClip)
            {
                ViewModel.SelectPlaylistClip(clip);
            }
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
                AnimateElementClickPulse(PlayPauseButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.Left:
                if (isShift) ViewModel.FineBackward();
                else ViewModel.SkipBackward();
                AnimateElementClickPulse(SkipBackwardButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                if (isShift) ViewModel.FineForward();
                else ViewModel.SkipForward();
                AnimateElementClickPulse(SkipForwardButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                ViewModel.SetVolume(ViewModel.Volume + 5);
                AnimateVolumeIconPop();
                e.Handled = true;
                break;

            case VirtualKey.Down:
                ViewModel.SetVolume(ViewModel.Volume - 5);
                AnimateVolumeIconPop();
                e.Handled = true;
                break;

            case VirtualKey.M:
                ViewModel.ToggleMute();
                AnimateVolumeIconPop();
                e.Handled = true;
                break;

            case VirtualKey.B:
                if (MarkMomentButton?.Flyout != null)
                {
                    MarkMomentButton.Flyout.ShowAt(MarkMomentButton);
                }
                AnimateElementClickPulse(MarkMomentButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.R:
                ViewModel.ToggleRepeatMode();
                AnimateElementClickPulse(RepeatButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.Q:
                ViewModel.ToggleSidebar();
                AnimateElementClickPulse(ClipQueueButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.F:
            case VirtualKey.F11:
                ToggleFullscreen();
                AnimateElementClickPulse(FullscreenButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (ViewModel.IsSidebarOpen)
                {
                    ViewModel.IsSidebarOpen = false;
                    AnimateElementClickPulse(CloseQueueButton, 0.92);
                }
                else if (App.Window.AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                    AnimateElementClickPulse(FullscreenButton, 0.92);
                }
                else
                {
                    AnimateElementClickPulse(BackToLibraryButton, 0.92);
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

    private Canvas? _timelineMarkersCanvas;
    private Canvas? TimelineMarkersCanvas => _timelineMarkersCanvas ??= FindDescendantByName<Canvas>(TimelineSlider, "TimelineMarkersCanvas");

    private void OnTimelineSliderLoaded(object sender, RoutedEventArgs e)
    {
        _timelineMarkersCanvas = FindDescendantByName<Canvas>(TimelineSlider, "TimelineMarkersCanvas");
        RenderTimelineMarkers();
    }

    private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            var result = FindDescendantByName<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void OnTimelineSliderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderTimelineMarkers();
    }

    private void RenderTimelineMarkers()
    {
        var canvas = TimelineMarkersCanvas;
        if (canvas == null) return;
        canvas.Children.Clear();
        if (ViewModel.CurrentClip == null || ViewModel.TotalTime <= TimeSpan.Zero) return;
        double sliderWidth = TimelineSlider.ActualWidth;
        if (sliderWidth <= 24) return;

        const double thumbWidth = 18.0;
        double trackPadding = thumbWidth / 2.0;
        double usableTrackWidth = sliderWidth - (trackPadding * 2.0);
        if (usableTrackWidth <= 0) return;

        double totalSecs = ViewModel.TotalTime.TotalSeconds;
        foreach (var bm in ViewModel.Bookmarks)
        {
            var baseColor = ParseColor(bm.ColorHex);
            if (bm.IsRange && bm.EndTimestamp.HasValue && bm.EndTimestamp.Value > bm.Timestamp)
            {
                double startFrac = Math.Clamp(bm.Timestamp.TotalSeconds / totalSecs, 0.0, 1.0);
                double endFrac = Math.Clamp(bm.EndTimestamp.Value.TotalSeconds / totalSecs, 0.0, 1.0);
                double startX = trackPadding + (startFrac * usableTrackWidth);
                double endX = trackPadding + (endFrac * usableTrackWidth);
                double rangeWidth = Math.Max(6.0, endX - startX);
                if (startX + rangeWidth > sliderWidth - trackPadding)
                {
                    rangeWidth = Math.Max(6.0, (sliderWidth - trackPadding) - startX);
                }

                var rangePill = new Border
                {
                    Width = rangeWidth,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(170, baseColor.R, baseColor.G, baseColor.B)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(rangePill, startX);
                Canvas.SetTop(rangePill, 0);
                canvas.Children.Add(rangePill);
            }
            else
            {
                double fraction = Math.Clamp(bm.Timestamp.TotalSeconds / totalSecs, 0.0, 1.0);
                double markerX = trackPadding + (fraction * usableTrackWidth) - 3.0;
                double markerY = 0.0;

                var marker = new Border
                {
                    Width = 6,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(baseColor),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(marker, markerX);
                Canvas.SetTop(marker, markerY);
                canvas.Children.Add(marker);
            }
        }
    }
}
