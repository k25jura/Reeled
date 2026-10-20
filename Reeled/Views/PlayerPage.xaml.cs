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
    private enum TimelinePickTarget { None, Point, RangeStart, RangeEnd }
    private TimelinePickTarget _activePickTarget = TimelinePickTarget.None;
    private ClipBookmark? _editingBookmark;
    private bool _isRenderingTimeline;


    private bool AreFlyoutsOrPopupsOpen =>
        _openFlyoutsCount > 0 ||
        _isSessionPanelOpen ||
        _isToolbarEditorOpen ||
        _isPickingOnTimeline ||
        (MomentsJumpPopup != null && MomentsJumpPopup.Visibility == Visibility.Visible) ||
        (MomentsFlyout != null && MomentsFlyout.IsOpen);

    private static bool IsReduceMotionEnabled() => Helpers.Motion.IsReduced;

    public PlayerPage()
    {
        ViewModel = App.GetService<PlayerViewModel>();
        InitializeComponent();

        // Frosted glass over the video: acrylic can't sample the video swap chain, see Helpers.VideoGlass
        _videoGlass = Helpers.VideoGlass.Create(PlayerVideoView);
        Helpers.VideoGlass.Wrap(TopHeaderBar, "Bar");
        Helpers.VideoGlass.Wrap(BottomControlBar, "Bar");
        Helpers.VideoGlass.Wrap(PlaylistQueueSidebar, "Bar");
        Helpers.VideoGlass.Wrap(SessionPanel, "Bar");
        Helpers.VideoGlass.Wrap(MomentsJumpPopup, "Flyout");
        Helpers.VideoGlass.Wrap(ActiveMomentPromptBadge, "Flyout");
        Helpers.VideoGlass.Wrap(TimelinePickerPromptBadge, "Flyout");
        foreach (var flyout in new[] { MomentsFlyout, TracksFlyout })
        {
            Helpers.VideoGlass.Wrap(flyout, 7);
        }

        InitializeToolbar();
        InitializeBarAnchors();
        InitializePanels();

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
            // One-shot: pointer movement restarts it
            _inactivityTimer.Stop();
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
                if (ViewModel.IsSidebarOpen && _isSessionPanelOpen) SetSessionPanelOpen(false);
                AnimateSidePanel(PlaylistQueueSidebar, SidebarTranslation, ViewModel.IsSidebarOpen);
                UpdateQueueSummary();
            }
            else if (e.PropertyName == nameof(PlayerViewModel.ProgressValue))
            {
                if (!TimelineSeekBar.IsDragging && !_isRenderingTimeline) TimelineSeekBar.Value = ViewModel.ProgressValue;
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsShuffleEnabled))
            {
                UpdateShuffleVisual();
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsExternalSubtitleActive))
            {
                ApplySubtitleOverlayStyle();
                UpdateSubtitleOverlay(ViewModel.CurrentTime);
            }
            else if (e.PropertyName == nameof(PlayerViewModel.CurrentTime))
            {
                // Paused seeks and frame steps (playback itself is tracked per frame)
                if (!_isRenderingTimeline && ViewModel.IsExternalSubtitleActive) UpdateSubtitleOverlay(ViewModel.CurrentTime);
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
                UpdateTimelineRendering();
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
                UpdateVolumeVisuals();
            }
        };

        ViewModel.Bookmarks.CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
            DispatcherQueue.TryEnqueue(RefreshMomentsPopupIfVisible);
        };

        SizeChanged += (s, e) =>
        {
            if (Visibility == Visibility.Visible)
            {
                RefreshVideoLayout();
                ApplySubtitleOverlayStyle();
                DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
                UpdateResponsiveLayout(e.NewSize.Width);
            }
        };


        // Catch pointer movements even if handled by inner controls to wake up cursor & controls
        this.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPagePointerMoved), true);
        this.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPagePointerExited), true);

        Loaded += (s, e) =>
        {
            // The page lives in a collapsed overlay from startup; only activate when it's actually shown
            if (App.Window is MainWindow mainWindow && mainWindow.IsPlayerVisible)
            {
                Activate();
            }
            ApplyLocalization();
            HookButtonPulses();
            ApplyToolbarLayout();
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

    /// <summary>The skip step is a setting, so the tooltips name the current value.</summary>
    private void UpdateSkipTooltips(Services.ILocalizationService loc)
    {
        int step = ViewModel.SkipStepSeconds;
        if (SkipBackwardButton != null) ToolTipService.SetToolTip(SkipBackwardButton, string.Format(loc["Player_Tooltip_SkipBackward"], step));
        if (SkipForwardButton != null) ToolTipService.SetToolTip(SkipForwardButton, string.Format(loc["Player_Tooltip_SkipForward"], step));
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
            UpdateSkipTooltips(loc);
            if (ClipQueueButton != null) ToolTipService.SetToolTip(ClipQueueButton, loc["Player_Tooltip_ClipQueue"]);
            if (MuteButton != null) ToolTipService.SetToolTip(MuteButton, loc["Player_Tooltip_MuteUnmute"]);
            if (FullscreenButton != null) ToolTipService.SetToolTip(FullscreenButton, loc["Player_Tooltip_Fullscreen"]);
            if (MomentsPromptDismissButton != null) ToolTipService.SetToolTip(MomentsPromptDismissButton, loc["Player_Dismiss"]);
            if (CloseQueueButton != null) ToolTipService.SetToolTip(CloseQueueButton, loc["Player_Tooltip_CloseQueue"]);

            // Moments Flyout
            if (MomentsFlyoutEmptyTitle != null) MomentsFlyoutEmptyTitle.Text = loc["Player_NoMomentsMarked"];
            if (MomentsFlyoutEmptySubtitle != null) MomentsFlyoutEmptySubtitle.Text = loc["Player_PressBToMark"];

            // Speed (session panel)
            if (PresetSpeed10 != null) PresetSpeed10.Content = loc["Player_PresetNormal"];
            if (CustomSpeedLabel != null) CustomSpeedLabel.Text = loc["Player_CustomSpeed"];
            if (ResetSpeedButton != null) ResetSpeedButton.Content = loc["Player_Reset"];


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
            LocalizePlayerExtras(loc);
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
        // The wedge folds away on narrow windows; the mute button stays
        bool narrow = width < 760;
        VolumeWedgeControl.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        VolumeValueText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Activate()
    {
        PlayerVideoView.Visibility = Visibility.Visible;
        this.Focus(FocusState.Programmatic);
        ViewModel.IsControlsVisible = true;
        _areControlsShowing = false;
        UpdateControlsVisibility(true, animate: false);
        UpdateBarAnchors(animate: false);
        SetFullscreenLayout(ViewModel.IsFullscreen);
        AnimateSidePanel(PlaylistQueueSidebar, SidebarTranslation, ViewModel.IsSidebarOpen, animate: false);
        RestartInactivityTimer();
        TimelineSeekBar.Value = ViewModel.ProgressValue;
        ApplyToolbarLayout();
        UpdateVolumeVisuals();
        UpdateQueueSummary();
        RefreshVideoLayout();
        UpdateResponsiveLayout(ActualWidth);
        CheckAndShowMomentsPrompt();
        DispatcherQueue.TryEnqueue(RenderTimelineMarkers);
        UpdateBackdropBlur();
        UpdateSkipTooltips(App.GetService<Services.ILocalizationService>());

        _isActive = true;
        _videoGlass?.SetActive(true);
        UpdateTimelineRendering();
    }

    public void SetVideoSurfaceVisibility(Visibility visibility)
    {
        PlayerVideoView.Visibility = visibility;
    }

    private bool _isActive;
    private Helpers.VideoGlass? _videoGlass;

    /// <summary>
    /// The per-frame timeline interpolation keeps the compositor rendering every frame, so it only
    /// runs while the player is shown and actually playing.
    /// </summary>
    private void UpdateTimelineRendering()
    {
        if (_videoGlass != null) _videoGlass.IsVideoPlaying = ViewModel.IsPlaying;
        if (_isActive && ViewModel.IsPlaying)
        {
            StartTimelineRendering();
        }
        else
        {
            StopTimelineRendering();
        }
    }

    private void StartTimelineRendering()
    {
        if (!_isRenderingTimeline)
        {
            _isRenderingTimeline = true;
            CompositionTarget.Rendering += OnTimelineRendering;
        }
    }

    private void StopTimelineRendering()
    {
        if (_isRenderingTimeline)
        {
            _isRenderingTimeline = false;
            CompositionTarget.Rendering -= OnTimelineRendering;
        }
    }

    private void OnTimelineRendering(object? sender, object e)
    {
        if (ViewModel.IsPlaying && !_isUserDraggingSlider && ViewModel.TotalTime > TimeSpan.Zero && ViewModel.LastPositionTimeTicks > 0)
        {
            double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - ViewModel.LastPositionTimeTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (elapsed >= 0 && elapsed < 0.6)
            {
                double currentSec = ViewModel.AnchorSeconds + (elapsed * ViewModel.PlaybackRate);
                double pct = (currentSec / ViewModel.TotalTime.TotalSeconds) * 100.0;
                TimelineSeekBar.Value = Math.Clamp(pct, 0.0, 100.0);
                if (ViewModel.IsExternalSubtitleActive) UpdateSubtitleOverlay(TimeSpan.FromSeconds(currentSec));
            }
        }
    }

    public void Deactivate()
    {
        _videoGlass?.SetActive(false);
        PlayerVideoView.Visibility = Visibility.Collapsed;
        _isActive = false;
        StopTimelineRendering();
        _inactivityTimer.Stop();
        HideOsdNow();
        _controlsStoryboard?.Stop();
        StopSidePanelAnimations();
        _momentsDismissTimer?.Stop();
        _momentsPopupStoryboard?.Stop();
        _lastPromptClipPath = null;
        HideMomentsPopup(animate: false);
        UpdateCursorHiddenState(false);

        // Reset control bar states so subsequent sessions begin healthy and visible
        ViewModel.IsControlsVisible = true;
        _areControlsShowing = true;
        TopHeaderBar.Visibility = Visibility.Visible;
        TopHeaderBar.Opacity = 1.0;
        BottomControlBar.Visibility = Visibility.Visible;
        BottomControlBar.Opacity = 1.0;

        try
        {
            if (App.Window is MainWindow mainWindow)
            {
                mainWindow.SetCursorHidden(false);
                mainWindow.SetCaptionControlsVisible(true, force: true);
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

    public void SetCursorHidden(bool hide)
    {
        UpdateCursorHiddenState(hide);
    }

    private void UpdateCursorHiddenState(bool hide)
    {
        var settingsService = App.GetService<Services.ILocalStorageService>();
        bool allowAutoHide = settingsService?.CurrentSettings?.AutoHideCursor ?? true;

        if (hide && allowAutoHide)
        {
            if (App.Window is MainWindow mainWindow && !mainWindow.IsWindowActive)
            {
                hide = false;
            }
        }

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
        _videoGlass?.SetSwapChain(e.SwapChainOptions);
    }

    private void OnPagePointerExited(object sender, PointerRoutedEventArgs e)
    {
        UpdateCursorHiddenState(false);
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.SetCursorHidden(false);
        }
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
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
            ShuffleButton,
            SkipBackwardButton,
            SkipForwardButton,
            FrameBackButton,
            FrameForwardButton,
            SnapshotButton,
            SpeedButton,
            MomentsButton,
            TracksButton,
            ClipQueueButton,
            SessionBarButton,
            SessionHeaderButton,
            PipButton,
            BackToLibraryButton,
            CloseQueueButton,
            CloseSessionButton,
            FullscreenButton,
            QueueRepeatButton,
            QueueShuffleButton,
            QueueAddButton,
            QueueSortButton,
            PresetSpeed05,
            PresetSpeed075,
            PresetSpeed10,
            PresetSpeed125,
            PresetSpeed15,
            PresetSpeed20,
            ResetSpeedButton,
            MarkPointModeButton,
            MarkRangeModeButton
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
        Helpers.Motion.Begin(sb);
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
            // Both brushes are shared resources, so updating them in place restyles every bar,
            // flyout, popup and badge that uses them
            if (Resources.TryGetValue("PlayerFlyoutBackdropBrush", out var flyoutObj) && flyoutObj is AcrylicBrush flyoutBrush)
            {
                Helpers.Backdrop.Apply(flyoutBrush, Windows.UI.Color.FromArgb(255, 26, 26, 26), Helpers.Backdrop.Surface.Flyout, overVideo: true);
            }
            if (Resources.TryGetValue("PlayerBarBackdropBrush", out var barObj) && barObj is AcrylicBrush barBrush)
            {
                Helpers.Backdrop.Apply(barBrush, Windows.UI.Color.FromArgb(255, 18, 18, 18), Helpers.Backdrop.Surface.Bar, overVideo: true);
            }
            _videoGlass?.Refresh();
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("UpdateBackdropBlur", ex);
        }
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
        Helpers.Motion.Begin(sb);
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
        Helpers.Motion.Begin(sb);
    }

    private void UpdateControlsVisibility(bool visible, bool animate = true, bool forceCursor = false)
    {
        _videoGlass?.Track(450);
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
                mainWindow.SetCursorHidden(false, force: forceCursor);
                mainWindow.SetCaptionControlsVisible(true);
            }
            else
            {
                bool shouldHideCursor = ViewModel.IsPlaying && mainWindow.IsWindowActive;
                UpdateCursorHiddenState(shouldHideCursor);
                mainWindow.SetCursorHidden(shouldHideCursor, force: forceCursor);
                mainWindow.SetCaptionControlsVisible(false);
            }
        }
        else
        {
            UpdateCursorHiddenState(!visible && ViewModel.IsPlaying);
        }

        _controlsStoryboard?.Stop();
        AnimateSubtitleLift(visible, animate);

        if (!animate)
        {
            TopHeaderBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BottomControlBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            TopHeaderBar.Opacity = visible ? 1.0 : 0.0;
            BottomControlBar.Opacity = visible ? 1.0 : 0.0;
            if (ActiveMomentBadgeTranslation != null)
            {
                ActiveMomentBadgeTranslation.Y = ActiveBadgeLift(visible);
            }
            return;
        }

        var sb = new Storyboard();
        bool reduceMotion = IsReduceMotionEnabled();
        var duration = TimeSpan.FromMilliseconds(visible ? (reduceMotion ? 120 : 200) : (reduceMotion ? 120 : 180));

        if (visible)
        {
            TopHeaderBar.Visibility = Visibility.Visible;
            BottomControlBar.Visibility = Visibility.Visible;

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var topOp = new DoubleAnimation { From = TopHeaderBar.Opacity, To = 1.0, Duration = duration, EasingFunction = easeOut };
            Storyboard.SetTarget(topOp, TopHeaderBar);
            Storyboard.SetTargetProperty(topOp, "Opacity");

            var botOp = new DoubleAnimation { From = BottomControlBar.Opacity, To = 1.0, Duration = duration, EasingFunction = easeOut };
            Storyboard.SetTarget(botOp, BottomControlBar);
            Storyboard.SetTargetProperty(botOp, "Opacity");

            sb.Children.Add(topOp);
            sb.Children.Add(botOp);

            if (ActiveMomentBadgeTranslation != null && !reduceMotion)
            {
                var badgeAnim = new DoubleAnimation
                {
                    From = ActiveMomentBadgeTranslation.Y,
                    To = ActiveBadgeLift(true),
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
                    ActiveMomentBadgeTranslation.Y = ActiveBadgeLift(true);
                }
            };
        }
        else
        {
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var topOp = new DoubleAnimation { From = TopHeaderBar.Opacity, To = 0.0, Duration = duration, EasingFunction = easeIn };
            Storyboard.SetTarget(topOp, TopHeaderBar);
            Storyboard.SetTargetProperty(topOp, "Opacity");

            var botOp = new DoubleAnimation { From = BottomControlBar.Opacity, To = 0.0, Duration = duration, EasingFunction = easeIn };
            Storyboard.SetTarget(botOp, BottomControlBar);
            Storyboard.SetTargetProperty(botOp, "Opacity");

            sb.Children.Add(topOp);
            sb.Children.Add(botOp);

            if (ActiveMomentBadgeTranslation != null && !reduceMotion)
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
        Helpers.Motion.Begin(sb);
    }

    public void SetFullscreenLayout(bool isFullscreen)
    {
        RefreshVideoLayout();
    }

    public void SyncControlsVisibility(bool forceCursor = false)
    {
        UpdateControlsVisibility(ViewModel.IsControlsVisible, animate: false, forceCursor: forceCursor);
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

    /// <summary>Fills the mark form for a new moment (not when editing or returning from the timeline).</summary>
    private void PrepareMarkForm()
    {
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
        _isPickingOnTimeline = true;
        MomentsFlyout?.Hide();

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
        _isPickingOnTimeline = false;
        if (TimelinePickerPromptBadge != null)
        {
            TimelinePickerPromptBadge.Visibility = Visibility.Collapsed;
            TimelinePickerPromptBadge.Opacity = 0.0;
        }

        ShowMomentsFlyout(MomentsTabMark, keepForm: true);
    }

    private void OnCancelTimelinePickerClick(object sender, RoutedEventArgs e)
    {
        _activePickTarget = TimelinePickTarget.None;
        _isPickingOnTimeline = false;
        if (TimelinePickerPromptBadge != null)
        {
            TimelinePickerPromptBadge.Visibility = Visibility.Collapsed;
            TimelinePickerPromptBadge.Opacity = 0.0;
        }

        ShowMomentsFlyout(MomentsTabMark, keepForm: true);
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

    private void UpdateMarkColorButtons() => MarkMomentColorChooser.SelectedValue = _selectedMarkColor;

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
        _editingBookmark = null;
        MomentsFlyout?.Hide();
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
        Helpers.Motion.Begin(sb);
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
        ActiveMomentText.Text = bookmark.DisplayLabel;
        ActiveMomentRange.Text = bookmark.FormattedRange;

        if (ActiveMomentPromptBadge.Visibility != Visibility.Visible || ActiveMomentPromptBadge.Opacity < 0.9)
        {
            _activeMomentBadgeStoryboard?.Stop();
            ActiveMomentPromptBadge.Visibility = Visibility.Visible;
            if (ActiveMomentBadgeTranslation != null)
            {
                ActiveMomentBadgeTranslation.Y = ActiveBadgeLift(_areControlsShowing);
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
            Helpers.Motion.Begin(sb);
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

            // Same flyout, other tab: the form slides in where the list was
            ShowMomentsFlyout(MomentsTabMark, keepForm: true);
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
            ShowMomentsPopup(clip);
        }
        else
        {
            HideMomentsPopup(animate: false);
        }
    }

    private void ShowMomentsPopup(GameClip clip)
    {
        _videoGlass?.Track(450);
        // Show a sorted snapshot: binding the clip's live collection let the card stay up empty when
        // moments were removed or re-sorted while it was visible.
        var moments = clip.Bookmarks.OrderBy(b => b.Timestamp).ToList();
        if (moments.Count == 0)
        {
            HideMomentsPopup(animate: false);
            return;
        }

        _momentsPopupStoryboard?.Stop();
        _momentsDismissTimer?.Stop();
        _inactivityTimer.Stop();
        ViewModel.IsControlsVisible = true;

        MomentsPopupItemsList.ItemsSource = moments;
        if (MomentsPromptCount != null) MomentsPromptCount.Text = moments.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        MomentsJumpPopup.Visibility = Visibility.Visible;

        var sb = new Storyboard();
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        AddDoubleAnimation(sb, MomentsJumpPopup, "Opacity", 1.0, 180, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "TranslateX", 0.0, 320, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "ScaleX", 1.0, 320, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "ScaleY", 1.0, 320, ease);

        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_momentsPopupStoryboard, sb)) return;
            _momentsPopupStoryboard = null;
            StartMomentsDismissTimer(7000);
        };

        _momentsPopupStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    /// <summary>Keeps a visible moments card in step with edits made while it is open.</summary>
    private void RefreshMomentsPopupIfVisible()
    {
        if (MomentsJumpPopup.Visibility != Visibility.Visible || ViewModel.CurrentClip == null) return;

        var moments = ViewModel.CurrentClip.Bookmarks.OrderBy(b => b.Timestamp).ToList();
        if (moments.Count == 0)
        {
            HideMomentsPopup(animate: true);
            return;
        }
        MomentsPopupItemsList.ItemsSource = moments;
        if (MomentsPromptCount != null) MomentsPromptCount.Text = moments.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    private static void AddDoubleAnimation(Storyboard sb, DependencyObject target, string property, double to, double ms, EasingFunctionBase ease)
    {
        var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        sb.Children.Add(anim);
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
        _videoGlass?.Track(450);
        _momentsDismissTimer?.Stop();
        _momentsPopupStoryboard?.Stop();

        void Finish()
        {
            MomentsJumpPopup.Visibility = Visibility.Collapsed;
            MomentsJumpPopup.Opacity = 0.0;
            MomentsPopupTranslation.TranslateX = -16;
            MomentsPopupTranslation.ScaleX = 0.98;
            MomentsPopupTranslation.ScaleY = 0.98;
            MomentsPopupItemsList.ItemsSource = null;
            if (!AreFlyoutsOrPopupsOpen && ViewModel.IsPlaying)
            {
                RestartInactivityTimer();
            }
        }

        if (!animate || MomentsJumpPopup.Visibility != Visibility.Visible)
        {
            Finish();
            return;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        AddDoubleAnimation(sb, MomentsJumpPopup, "Opacity", 0.0, 150, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "TranslateX", -10.0, 170, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "ScaleX", 0.98, 170, ease);
        AddDoubleAnimation(sb, MomentsPopupTranslation, "ScaleY", 0.98, 170, ease);

        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_momentsPopupStoryboard, sb)) return;
            _momentsPopupStoryboard = null;
            Finish();
        };

        _momentsPopupStoryboard = sb;
        Helpers.Motion.Begin(sb);
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

    private void OnPlayerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(this);
        int delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta == 0) return;

        switch (App.GetService<Services.ILocalStorageService>().CurrentSettings.PlayerWheelAction)
        {
            case Models.WheelAction.Seek:
                if (delta > 0) ViewModel.SkipForward();
                else ViewModel.SkipBackward();
                e.Handled = true;
                break;
            case Models.WheelAction.None:
                break;
            default:
                ViewModel.SetVolume(ViewModel.Volume + (delta > 0 ? 5 : -5));
                AnimateVolumeIconPop();
                e.Handled = true;
                break;
        }
    }

    private void OnPlayerPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
    }

    public void HandleKeyDown(KeyRoutedEventArgs e)
    {
        // Never steal keystrokes from text entry (moment titles, hex color input)
        if (e.OriginalSource is TextBox or PasswordBox or AutoSuggestBox) return;

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShift = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool isCtrl = ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (isCtrl)
        {
            if (e.Key == VirtualKey.O)
            {
                OpenMediaFromPlayer();
                e.Handled = true;
            }
            return;
        }

        if (e.Key is >= VirtualKey.Number0 and <= VirtualKey.Number9 || e.Key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            int digit = e.Key >= VirtualKey.NumberPad0 ? e.Key - VirtualKey.NumberPad0 : e.Key - VirtualKey.Number0;
            ViewModel.SeekToFraction(digit / 10.0);
            e.Handled = true;
            return;
        }

        switch ((int)e.Key)
        {
            case VK_OEM_COMMA:
                ViewModel.StepFrame(forward: false);
                e.Handled = true;
                return;
            case VK_OEM_PERIOD:
                ViewModel.StepFrame(forward: true);
                e.Handled = true;
                return;
            case VK_OEM_4: // [
                ViewModel.AdjustSpeed(-0.25);
                UpdateSpeedPresetHighlight();
                e.Handled = true;
                return;
            case VK_OEM_6: // ]
                ViewModel.AdjustSpeed(0.25);
                UpdateSpeedPresetHighlight();
                e.Handled = true;
                return;
            case VK_OEM_5: // \
                ViewModel.ResetSpeed();
                UpdateSpeedPresetHighlight();
                e.Handled = true;
                return;
        }

        switch (e.Key)
        {
            case VirtualKey.Home:
                ViewModel.SeekToFraction(0.0);
                e.Handled = true;
                break;

            case VirtualKey.End:
                ViewModel.SeekToFraction(1.0);
                e.Handled = true;
                break;

            case VirtualKey.N:
            case VirtualKey.PageDown:
                ViewModel.PlayNext();
                AnimateElementClickPulse(NextClipButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.P:
            case VirtualKey.PageUp:
                ViewModel.PlayPrevious();
                AnimateElementClickPulse(PreviousClipButton, 0.92);
                e.Handled = true;
                break;

            case VirtualKey.H:
                // Hide the controls right away (move the mouse to bring them back)
                _inactivityTimer.Stop();
                ViewModel.IsControlsVisible = false;
                e.Handled = true;
                break;

            case VirtualKey.L:
                // A-B loop: first press sets A, second sets B, third clears
                if (!ViewModel.LoopStart.HasValue) ViewModel.SetLoopPoint(isStart: true);
                else if (!ViewModel.LoopEnd.HasValue) ViewModel.SetLoopPoint(isStart: false);
                else ViewModel.ClearLoop();
                UpdateLoopStatus();
                e.Handled = true;
                break;

            case VirtualKey.S:
                ViewModel.Stop();
                AnimateElementClickPulse(StopClipButton, 0.92);
                e.Handled = true;
                break;

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
                ShowMomentsFlyout(MomentsTabMark);
                AnimateElementClickPulse(MomentsButton, 0.92);
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

            case VirtualKey.Enter:
                var menuState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
                if (menuState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    ToggleFullscreen();
                    AnimateElementClickPulse(FullscreenButton, 0.92);
                    e.Handled = true;
                }
                break;

            case VirtualKey.Back:
                AnimateElementClickPulse(BackToLibraryButton, 0.92);
                ViewModel.BackToHome();
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (_isToolbarEditorOpen)
                {
                    CloseToolbarEditor();
                }
                else if (_isSessionPanelOpen)
                {
                    SetSessionPanelOpen(false);
                }
                else if (ViewModel.IsSidebarOpen)
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

    private const int VK_OEM_COMMA = 0xBC;
    private const int VK_OEM_PERIOD = 0xBE;
    private const int VK_OEM_4 = 0xDB;
    private const int VK_OEM_5 = 0xDC;
    private const int VK_OEM_6 = 0xDD;

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleKeyDown(e);
    }

    private Canvas TimelineMarkersCanvas => TimelineSeekBar.MarkersCanvas;

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


    private void RenderTimelineMarkers()
    {
        var canvas = TimelineMarkersCanvas;
        if (canvas == null) return;
        canvas.Children.Clear();
        if (ViewModel.CurrentClip == null || ViewModel.TotalTime <= TimeSpan.Zero) return;
        double sliderWidth = TimelineSeekBar.ActualWidth;
        if (sliderWidth <= 24) return;

        const double trackPadding = 0.0;
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
