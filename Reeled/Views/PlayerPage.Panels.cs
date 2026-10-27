using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

/// <summary>
/// Player panels: the right-hand side panels (clip queue, session settings), the tabbed tracks flyout
/// (video / audio / subtitles) and the tabbed moments flyout (list / mark).
/// </summary>
public sealed partial class PlayerPage
{
    private const int SidePanelWidth = 340;
    private const int MomentsTabList = 0;
    private const int MomentsTabMark = 1;
    private const int TracksTabVideo = 0, TracksTabAudio = 1, TracksTabSubtitles = 2;
    private const int SessionTabPlayback = 0, SessionTabVideo = 1, SessionTabAudio = 2, SessionTabSubtitles = 3;

    private bool _isSessionPanelOpen;
    private bool _tracksTabChosen;
    private bool _isPickingOnTimeline;
    private bool _syncingSession = true; // until the page has finished loading its XAML
    private readonly Dictionary<FrameworkElement, Storyboard> _panelStoryboards = new();
    private readonly List<Slider> _eqBandSliders = new();

    private static Services.ILocalStorageService Storage => App.GetService<Services.ILocalStorageService>();
    private static Services.ILocalizationService Loc => App.GetService<Services.ILocalizationService>();

    private void InitializePanels()
    {
        MomentsTabs.SelectionChanged += (s, i) => SwitchMomentsTab(i);
        TracksTabs.SelectionChanged += (s, i) => SwitchTracksTab(i);
        SessionTabs.SelectionChanged += (s, i) => SwitchSessionTab(i);
        BuildQueueSortMenu();
        BuildEqualizerBands();
        InitializeMomentColors();
        BuildSubtitleColors();
        ViewModel.Playlist.CollectionChanged += (s, e) => UpdateQueueSummary();
        ApplyRememberedSession();
        _syncingSession = false;
    }

    private void LocalizePlayerExtras(Services.ILocalizationService loc)
    {
        MomentsTabs.SetTabs(new[] { ("", loc["Player_MomentsTitle"]), ("", loc["Player_MarkMoment_Title"]) });
        TracksTabs.SetTabs(new[] { ("", loc["Player_Tab_Video"]), ("", loc["Player_Tab_Audio"]), ("", loc["Player_Tab_Subtitles"]) });
        SessionTabs.SetTabs(new[]
        {
            ("", loc["Player_Tab_Playback"]), ("", loc["Player_Tab_Video"]),
            ("", loc["Player_Tab_Audio"]), ("", loc["Player_Tab_Subtitles"])
        });

        ToolTipService.SetToolTip(MomentsButton, loc["Player_Tooltip_Moments"]);
        ToolTipService.SetToolTip(TracksButton, loc["Player_Tooltip_Tracks"]);
        ToolTipService.SetToolTip(ShuffleButton, loc["Player_Tooltip_Shuffle"]);
        ToolTipService.SetToolTip(SpeedButton, loc["Player_Tooltip_PlaybackSpeed"]);
        ToolTipService.SetToolTip(SessionBarButton, loc["Player_Tooltip_Session"]);
        ToolTipService.SetToolTip(SessionHeaderButton, loc["Player_Tooltip_Session"]);
        ToolTipService.SetToolTip(SnapshotButton, loc["Player_Tooltip_Snapshot"]);
        ToolTipService.SetToolTip(ShareBarButton, loc["Player_Share"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ShareBarButton, loc["Player_Share"]);
        ToolTipService.SetToolTip(PipButton, loc["Player_Tooltip_Pip"]);
        ToolTipService.SetToolTip(FrameBackButton, loc["Player_Tooltip_FrameBack"]);
        ToolTipService.SetToolTip(FrameForwardButton, loc["Player_Tooltip_FrameForward"]);
        ToolTipService.SetToolTip(QueueSortButton, loc["Player_Queue_Sort"]);
        ToolTipService.SetToolTip(QueueShuffleButton, loc["Player_Tooltip_Shuffle"]);
        ToolTipService.SetToolTip(QueueAddButton, loc["Player_Queue_Add"]);
        ToolTipService.SetToolTip(CloseSessionButton, loc["Player_Close"]);
        SessionHeaderText.Text = loc["Player_SessionButton"];
        SessionPanelTitle.Text = loc["Player_SessionTitle"];
        TracksMoreButton.Content = loc["Player_MoreSettings"];

        SessionSpeedTitle.Text = loc["Player_PlaybackSpeedTitle"];
        SessionRepeatTitle.Text = loc["Player_Session_WhenEnds"];
        SessionLoopTitle.Text = loc["Player_Session_Loop"];
        LoopSetAButton.Content = loc["Player_Session_LoopA"];
        LoopSetBButton.Content = loc["Player_Session_LoopB"];
        LoopClearButton.Content = loc["Player_Session_LoopClear"];
        SessionAdjustTitle.Text = loc["Player_Session_Picture"];
        VideoBrightnessLabel.Text = loc["Player_Session_Brightness"];
        VideoContrastLabel.Text = loc["Player_Session_Contrast"];
        VideoSaturationLabel.Text = loc["Player_Session_Saturation"];
        VideoGammaLabel.Text = loc["Player_Session_Gamma"];
        VideoHueLabel.Text = loc["Player_Session_Hue"];
        SessionAspectTitle.Text = loc["Player_Session_Aspect"];
        VideoResetButton.Content = loc["Player_Session_ResetPicture"];
        VideoRememberLabel.Text = loc["Player_Session_Remember"];
        EqualizerTitle.Text = loc["Player_Session_Equalizer"];
        EqualizerPreampLabel.Text = loc["Player_Session_Preamp"];
        SessionAudioDelayTitle.Text = loc["Player_Session_Sync"];
        AudioDelayLabel.Text = loc["Player_Session_AudioDelay"];
        AudioRememberLabel.Text = loc["Player_Session_Remember"];
        SessionSubSyncTitle.Text = loc["Player_Session_Sync"];
        SubtitleDelayLabel.Text = loc["Player_Session_SubtitleDelay"];
        SessionSubStyleTitle.Text = loc["Player_Session_Style"];
        SubtitleSizeLabel.Text = loc["Player_Session_Size"];
        SubtitleColorLabel.Text = loc["Player_Session_Color"];
        SubtitleBackLabel.Text = loc["Player_Session_Background"];
        SubtitleBoldLabel.Text = loc["Player_Session_Bold"];
        SubtitleStyleHint.Text = loc["Player_Session_StyleHint"];

        FillSessionCombos(loc);
        BuildQueueSortMenu();
        UpdateLoopStatus();
        UpdateQueueSummary();
        SyncSessionControls();
    }

    // ---------------------------------------------------------------------------------------------
    // Side panels
    // ---------------------------------------------------------------------------------------------

    /// <summary>Slides a right-hand panel in or out; the bottom bar makes room for it.</summary>
    private void AnimateSidePanel(Border panel, TranslateTransform translation, bool open, bool animate = true)
    {
        _videoGlass?.Track(500);
        if (_panelStoryboards.TryGetValue(panel, out var running)) running.Stop();
        bool anyOpen = open || (panel == SessionPanel ? ViewModel.IsSidebarOpen : _isSessionPanelOpen);

        if (!animate || Helpers.Motion.IsReduced)
        {
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            panel.Opacity = open ? 1 : 0;
            translation.X = open ? 0 : SidePanelWidth;
            BottomControlBarQueueSpacer.Width = anyOpen ? SidePanelWidth : 0;
            if (open && panel == PlaylistQueueSidebar) ScrollQueueToCurrent();
            return;
        }

        var sb = new Storyboard();
        if (open)
        {
            panel.Visibility = Visibility.Visible;
            if (panel == PlaylistQueueSidebar) ScrollQueueToCurrent();
            sb.Children.Add(Helpers.Motion.Spline(translation, "X", 0, Helpers.Motion.Long, from: translation.X));
            sb.Children.Add(Helpers.Motion.Spline(panel, "Opacity", 1, Helpers.Motion.Medium, from: panel.Opacity));
        }
        else
        {
            sb.Children.Add(Helpers.Motion.Spline(translation, "X", SidePanelWidth, Helpers.Motion.Short + 40, accelerate: true));
            sb.Children.Add(Helpers.Motion.Spline(panel, "Opacity", 0, Helpers.Motion.Short, accelerate: true));
        }
        sb.Children.Add(Helpers.Motion.Spline(BottomControlBarQueueSpacer, "Width", anyOpen ? SidePanelWidth : 0,
            open ? Helpers.Motion.Long : Helpers.Motion.Short + 40, from: BottomControlBarQueueSpacer.ActualWidth, accelerate: !anyOpen, dependent: true));
        sb.Completed += (s, e) =>
        {
            _panelStoryboards.Remove(panel);
            if (!open) panel.Visibility = Visibility.Collapsed;
        };
        _panelStoryboards[panel] = sb;
        Helpers.Motion.BeginExact(sb);
    }

    private void StopSidePanelAnimations()
    {
        foreach (var sb in _panelStoryboards.Values.ToList()) sb.Stop();
        _panelStoryboards.Clear();
    }

    private void SetSessionPanelOpen(bool open)
    {
        if (_isSessionPanelOpen == open) return;
        _isSessionPanelOpen = open;
        if (open)
        {
            if (ViewModel.IsSidebarOpen) ViewModel.IsSidebarOpen = false;
            SyncSessionControls();
            _inactivityTimer.Stop();
            ViewModel.IsControlsVisible = true;
        }
        AnimateSidePanel(SessionPanel, SessionPanelTranslation, open);
        if (!open && ViewModel.IsPlaying && !AreFlyoutsOrPopupsOpen) RestartInactivityTimer();
    }

    private void OpenSessionPanel(int tab)
    {
        SessionTabs.SelectedIndex = tab;
        SwitchSessionTab(tab);
        SetSessionPanelOpen(true);
    }

    private void OnSessionButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isSessionPanelOpen) SetSessionPanelOpen(false);
        else OpenSessionPanel(SessionTabs.SelectedIndex < 0 ? SessionTabPlayback : SessionTabs.SelectedIndex);
    }

    private void OnCloseSessionClick(object sender, RoutedEventArgs e) => SetSessionPanelOpen(false);

    private void OnCloseQueueClick(object sender, RoutedEventArgs e) => ViewModel.IsSidebarOpen = false;

    // ---------------------------------------------------------------------------------------------
    // Queue
    // ---------------------------------------------------------------------------------------------

    private void ScrollQueueToCurrent()
    {
        if (ViewModel.CurrentClip != null) PlaylistQueueListView.ScrollIntoView(ViewModel.CurrentClip);
    }

    private void UpdateQueueSummary()
    {
        if (QueueSummaryText == null) return;
        var total = TimeSpan.FromSeconds(ViewModel.Playlist.Sum(c => c.Duration.TotalSeconds));
        string duration = total.TotalHours >= 1
            ? string.Format(Loc["Player_Queue_DurationHours"], (int)total.TotalHours, total.Minutes)
            : string.Format(Loc["Player_Queue_DurationMinutes"], Math.Max(1, (int)Math.Round(total.TotalMinutes)));
        int count = ViewModel.Playlist.Count;
        QueueSummaryText.Text = $"{count} {Loc.FormatPlural("Plural_Clip", count)}  ·  {duration}";
    }

    private void OnQueueItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameClip clip && clip != ViewModel.CurrentClip) ViewModel.SelectPlaylistClip(clip);
    }

    private void OnQueueDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Order changed by dragging: previous / next follow the new order
        UpdateQueueSummary();
    }

    private void OnQueueItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not GameClip clip) return;
        var loc = Loc;
        var menu = new MenuFlyout();
        var play = new MenuFlyoutItem { Text = loc["Player_Queue_Play"], Icon = new FontIcon { Glyph = "" } };
        play.Click += (s, a) => ViewModel.SelectPlaylistClip(clip);
        var reveal = new MenuFlyoutItem { Text = loc["FolderMenu_RevealExplorer"], Icon = new FontIcon { Glyph = "" } };
        reveal.Click += (s, a) => RevealInExplorer(clip.FilePath);
        var remove = new MenuFlyoutItem
        {
            Text = loc["Player_Queue_Remove"],
            Icon = new FontIcon { Glyph = "" },
            IsEnabled = !(ReferenceEquals(clip, ViewModel.CurrentClip) && ViewModel.Playlist.Count == 1)
        };
        remove.Click += (s, a) => ViewModel.RemoveFromPlaylist(clip);
        menu.Items.Add(play);
        menu.Items.Add(reveal);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        menu.ShowAt(el, e.GetPosition(el));
        e.Handled = true;
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private async void OnQueueAddClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var ext in new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v", ".ts" }) picker.FileTypeFilter.Add(ext);
        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;
        ViewModel.AddFilesToPlaylist(files.Select(f => f.Path));
    }

    private void BuildQueueSortMenu()
    {
        if (QueueSortMenu == null) return;
        QueueSortMenu.Items.Clear();
        var loc = Loc;
        foreach (var (sort, key, glyph) in new[]
        {
            (PlayerViewModel.PlaylistSort.Name, "Player_Queue_SortName", ""),
            (PlayerViewModel.PlaylistSort.NameDescending, "Player_Queue_SortNameDesc", ""),
            (PlayerViewModel.PlaylistSort.Newest, "Player_Queue_SortNewest", ""),
            (PlayerViewModel.PlaylistSort.Oldest, "Player_Queue_SortOldest", ""),
            (PlayerViewModel.PlaylistSort.Longest, "Player_Queue_SortLongest", ""),
            (PlayerViewModel.PlaylistSort.Shortest, "Player_Queue_SortShortest", ""),
            (PlayerViewModel.PlaylistSort.Reverse, "Player_Queue_SortReverse", ""),
        })
        {
            var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph } };
            item.Click += (s, e) => ViewModel.SortPlaylist(sort);
            if (sort == PlayerViewModel.PlaylistSort.Reverse) QueueSortMenu.Items.Add(new MenuFlyoutSeparator());
            QueueSortMenu.Items.Add(item);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Moments flyout (list / mark)
    // ---------------------------------------------------------------------------------------------

    private int _pendingMomentsTab = MomentsTabList;
    private bool _keepMarkForm;

    private void ShowMomentsFlyout(int tab, bool keepForm = false)
    {
        _pendingMomentsTab = tab;
        _keepMarkForm = keepForm;
        if (MomentsFlyout.IsOpen)
        {
            MomentsTabs.SelectedIndex = tab;
            SwitchMomentsTab(tab);
            return;
        }
        MomentsFlyout.ShowAt(MomentsButton);
    }

    private void OnMomentsFlyoutOpening(object sender, object e)
    {
        int tab = _pendingMomentsTab;
        _pendingMomentsTab = MomentsTabList;
        MomentsTabs.SelectedIndex = tab;
        SwitchMomentsTab(tab, animate: false);
    }

    private void OnMomentsFlyoutClosed(object sender, object e)
    {
        OnFlyoutClosed(sender, e);
        if (_activePickTarget == TimelinePickTarget.None)
        {
            _editingBookmark = null;
            _keepMarkForm = false;
        }
    }

    private void SwitchMomentsTab(int tab) => SwitchMomentsTab(tab, animate: true);

    private void SwitchMomentsTab(int tab, bool animate)
    {
        if (tab == MomentsTabMark)
        {
            if (!_keepMarkForm) _editingBookmark = null;
            PrepareMarkForm();
            var loc = Loc;
            MarkMomentFlyoutTitle.Text = _editingBookmark != null ? loc["Player_EditMomentTitle"] : loc["Player_MarkMoment_Title"];
            MarkMomentSaveButton.Content = _editingBookmark != null ? loc["Player_EditMomentSave"] : loc["Player_MarkMoment_Save"];
            _keepMarkForm = false;
        }
        var panels = new List<FrameworkElement> { MomentsListPanel, MarkMomentPanel };
        int from = MomentsListPanel.Visibility == Visibility.Visible ? 0 : 1;
        TabStripSwitch(panels, from, tab, animate);
    }

    private static void TabStripSwitch(List<FrameworkElement> panels, int from, int to, bool animate)
    {
        if (!animate)
        {
            for (int i = 0; i < panels.Count; i++) panels[i].Visibility = i == to ? Visibility.Visible : Visibility.Collapsed;
            panels[to].Opacity = 1;
            return;
        }
        Controls.TabStrip.SwitchContent(panels, from, to);
    }

    // ---------------------------------------------------------------------------------------------
    // Tracks flyout (video / audio / subtitles)
    // ---------------------------------------------------------------------------------------------

    private void OnTracksFlyoutOpening(object sender, object e)
    {
        ViewModel.RefreshAudioTracks();
        ViewModel.RefreshSubtitleTracks();
        if (!_tracksTabChosen)
        {
            // Audio is what people reach for most; after that the flyout remembers the last tab
            _tracksTabChosen = true;
            TracksTabs.SelectedIndex = TracksTabAudio;
        }
        BuildTracksPanel(TracksTabs.SelectedIndex);
        SwitchTracksTab(TracksTabs.SelectedIndex, animate: false);
    }

    private void SwitchTracksTab(int tab) => SwitchTracksTab(tab, animate: true);

    private void SwitchTracksTab(int tab, bool animate)
    {
        BuildTracksPanel(tab);
        var panels = new List<FrameworkElement> { TracksVideoPanel, TracksAudioPanel, TracksSubtitlesPanel };
        int from = panels.FindIndex(p => p.Visibility == Visibility.Visible);
        TabStripSwitch(panels, Math.Max(0, from), tab, animate);
    }

    private TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 255, 255, 255)),
        Margin = new Thickness(6, 8, 0, 4)
    };

    /// <summary>A full-width row with a check mark when selected (VLC concept, our styling).</summary>
    private Button OptionRow(string text, bool selected, Action onClick, string? glyph = null)
    {
        var check = new FontIcon
        {
            Glyph = glyph ?? "",
            FontSize = 12,
            Width = 16,
            Foreground = Helpers.ThemeBrushes.Get("AccentTextFillColorPrimaryBrush", ElementTheme.Dark),
            Opacity = selected ? 1 : 0
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(check);
        row.Children.Add(label);
        var button = new Button
        {
            Content = row,
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 7, 8, 7),
            CornerRadius = new CornerRadius(5)
        };
        button.Click += (s, e) => onClick();
        return button;
    }

    private void BuildTracksPanel(int tab)
    {
        var loc = Loc;
        switch (tab)
        {
            case TracksTabVideo:
                TracksVideoPanel.Children.Clear();
                TracksVideoPanel.Children.Add(SectionLabel(loc["Player_Session_Aspect"]));
                string? current = ViewModel.PlaybackService.AspectRatio;
                foreach (var (value, label) in AspectOptions(loc))
                {
                    var v = value;
                    TracksVideoPanel.Children.Add(OptionRow(label, current == v, () =>
                    {
                        SetAspect(v);
                        BuildTracksPanel(TracksTabVideo);
                    }));
                }
                break;

            case TracksTabAudio:
                TracksAudioPanel.Children.Clear();
                TracksAudioPanel.Children.Add(SectionLabel(loc["Player_Tracks_Embedded"]));
                var active = ViewModel.ActiveAudioTracks;
                if (ViewModel.AudioTracks.Count == 0)
                {
                    TracksAudioPanel.Children.Add(SectionLabel(loc["Player_NoAudioTracks"]));
                }
                foreach (var track in ViewModel.AudioTracks)
                {
                    int id = track.Id;
                    // Tracks can be mixed: each row toggles, so several can be on at once
                    TracksAudioPanel.Children.Add(OptionRow(track.DisplayName, active.Contains(id), () =>
                    {
                        ViewModel.ToggleAudioTrack(id);
                        BuildTracksPanel(TracksTabAudio);
                    }));
                }
                TracksAudioPanel.Children.Add(OptionRow(loc["Player_DisableAudio"], active.Count == 0, () =>
                {
                    ViewModel.DisableAudio();
                    BuildTracksPanel(TracksTabAudio);
                }));

                var devices = ViewModel.PlaybackService.GetAudioOutputDevices();
                if (devices.Count > 0)
                {
                    var header = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                    header.Children.Add(SectionLabel(loc["Player_Tracks_OutputDevice"]));
                    var refresh = new Button
                    {
                        Content = new FontIcon { Glyph = "", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                        Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Padding = new Thickness(7),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    ToolTipService.SetToolTip(refresh, loc["Player_Tracks_RefreshDevices"]);
                    refresh.Click += (s, e) => BuildTracksPanel(TracksTabAudio);
                    header.Children.Add(refresh);
                    TracksAudioPanel.Children.Add(header);

                    string? currentDevice = ViewModel.PlaybackService.CurrentAudioOutputDevice;
                    foreach (var (deviceId, name) in devices)
                    {
                        string idCopy = deviceId;
                        bool isCurrent = string.Equals(currentDevice, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                         (string.IsNullOrEmpty(currentDevice) && string.IsNullOrEmpty(deviceId));
                        TracksAudioPanel.Children.Add(OptionRow(name, isCurrent, () =>
                        {
                            if (ViewModel.PlaybackService.SetAudioOutputDevice(idCopy))
                            {
                                var prefs = Storage.CurrentSettings.PlayerSession;
                                prefs.AudioOutputDevice = idCopy;
                                _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
                                ViewModel.ShowToast(name, kind: OsdKind.Tracks);
                            }
                            DispatcherQueue.TryEnqueue(() => BuildTracksPanel(TracksTabAudio));
                        }));
                    }
                }
                break;

            case TracksTabSubtitles:
                TracksSubtitlesPanel.Children.Clear();
                TracksSubtitlesPanel.Children.Add(SectionLabel(loc["Player_Tracks_Embedded"]));
                TracksSubtitlesPanel.Children.Add(OptionRow(loc["Player_Tracks_None"], ViewModel.SelectedSubtitleTrack == null && !ViewModel.IsExternalSubtitleActive, () =>
                {
                    ViewModel.SelectSubtitleTrack(-1);
                    BuildTracksPanel(TracksTabSubtitles);
                }));
                if (ViewModel.ExternalSubtitleCues != null)
                {
                    TracksSubtitlesPanel.Children.Add(OptionRow(ViewModel.ExternalSubtitleName ?? "", ViewModel.IsExternalSubtitleActive, () =>
                    {
                        ViewModel.SelectExternalSubtitle();
                        BuildTracksPanel(TracksTabSubtitles);
                    }));
                }
                foreach (var track in ViewModel.SubtitleTracks)
                {
                    int id = track.Id;
                    TracksSubtitlesPanel.Children.Add(OptionRow(track.DisplayName, track.IsSelected && !ViewModel.IsExternalSubtitleActive, () =>
                    {
                        ViewModel.SelectSubtitleTrack(id);
                        BuildTracksPanel(TracksTabSubtitles);
                    }));
                }
                TracksSubtitlesPanel.Children.Add(OptionRow(loc["Player_AddSubtitleFile"], false, async () =>
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
                    await ViewModel.AddSubtitleFileAsync(hwnd);
                    BuildTracksPanel(TracksTabSubtitles);
                }, glyph: ""));
                // The add row shows its plus sign; the others only show a check when selected
                if (TracksSubtitlesPanel.Children.LastOrDefault() is Button add && add.Content is StackPanel sp && sp.Children[0] is FontIcon plus)
                {
                    plus.Opacity = 1;
                }
                break;
        }
    }

    private void OnTracksMoreClick(object sender, RoutedEventArgs e)
    {
        TracksFlyout.Hide();
        OpenSessionPanel(TracksTabs.SelectedIndex switch
        {
            TracksTabVideo => SessionTabVideo,
            TracksTabSubtitles => SessionTabSubtitles,
            _ => SessionTabAudio
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Session settings panel
    // ---------------------------------------------------------------------------------------------

    private void SwitchSessionTab(int tab) => SwitchSessionTab(tab, animate: true);

    private void SwitchSessionTab(int tab, bool animate)
    {
        var panels = new List<FrameworkElement> { SessionPlaybackPanel, SessionVideoPanel, SessionAudioPanel, SessionSubtitlesPanel };
        int from = panels.FindIndex(p => p.Visibility == Visibility.Visible);
        TabStripSwitch(panels, Math.Max(0, from), Math.Clamp(tab, 0, panels.Count - 1), animate);
        if (tab == SessionTabPlayback) UpdateSpeedPresetHighlight();
    }

    private static (string? Value, string Label)[] AspectOptions(Services.ILocalizationService loc) => new (string?, string)[]
    {
        (null, loc["Player_Aspect_Default"]),
        ("16:9", "16:9"),
        ("21:9", "21:9"),
        ("4:3", "4:3"),
        ("16:10", "16:10"),
        ("1:1", "1:1"),
    };

    private void FillSessionCombos(Services.ILocalizationService loc)
    {
        _syncingSession = true;
        SessionRepeatCombo.ItemsSource = new[] { loc["Player_Session_RepeatOff"], loc["Player_Session_RepeatAll"], loc["Player_Session_RepeatOne"] };
        VideoAspectCombo.ItemsSource = AspectOptions(loc).Select(a => a.Label).ToArray();
        var presets = new List<string> { loc["Player_Eq_Custom"] };
        int count = ViewModel.PlaybackService.EqualizerPresetCount;
        for (int i = 0; i < count; i++) presets.Add(ViewModel.PlaybackService.EqualizerPresetName(i));
        EqualizerPresetCombo.ItemsSource = presets;
        SubtitleBackCombo.ItemsSource = new[] { loc["Player_Sub_Outline"], loc["Player_Sub_Box"], loc["Player_Sub_None"] };
        _syncingSession = false;
    }

    /// <summary>Puts the current values into the panel's controls without triggering their handlers.</summary>
    private void SyncSessionControls()
    {
        if (SessionRepeatCombo == null) return;
        _syncingSession = true;
        var svc = ViewModel.PlaybackService;
        var prefs = Storage.CurrentSettings.PlayerSession;

        SessionRepeatCombo.SelectedIndex = (int)ViewModel.RepeatMode;
        var adjust = svc.VideoAdjust;
        VideoBrightnessSlider.Value = adjust.Brightness;
        VideoContrastSlider.Value = adjust.Contrast;
        VideoSaturationSlider.Value = adjust.Saturation;
        VideoGammaSlider.Value = adjust.Gamma;
        VideoHueSlider.Value = adjust.Hue;
        var aspects = AspectOptions(Loc);
        VideoAspectCombo.SelectedIndex = Math.Max(0, Array.FindIndex(aspects, a => a.Value == svc.AspectRatio));
        VideoRememberToggle.IsOn = prefs.RememberVideo;

        EqualizerToggle.IsOn = prefs.EqualizerEnabled;
        EqualizerPresetCombo.SelectedIndex = prefs.EqualizerPreset + 1;
        for (int i = 0; i < _eqBandSliders.Count && i < prefs.EqualizerBands.Length; i++) _eqBandSliders[i].Value = prefs.EqualizerBands[i];
        EqualizerPreampSlider.Value = prefs.EqualizerPreamp;
        SetEqualizerControlsEnabled(prefs.EqualizerEnabled);
        AudioDelaySlider.Value = svc.AudioDelayMs;
        AudioRememberToggle.IsOn = prefs.RememberAudio;

        SubtitleDelaySlider.Value = svc.SubtitleDelayMs;
        var style = svc.SubtitleStyle;
        SubtitleSizeSlider.Value = style.Scale * 100;
        SubtitleBackCombo.SelectedIndex = (int)style.Background;
        SubtitleBoldToggle.IsOn = style.Bold;
        UpdateSubtitleColorSelection();

        foreach (var s in new[] { VideoBrightnessSlider, VideoContrastSlider, VideoSaturationSlider, VideoGammaSlider, VideoHueSlider,
                                  EqualizerPreampSlider, AudioDelaySlider, SubtitleDelaySlider, SubtitleSizeSlider })
        {
            UpdateSliderValueText(s);
        }
        UpdateLoopStatus();
        _syncingSession = false;
    }

    private void UpdateSliderValueText(Slider slider)
    {
        if (FindName(slider.Name + "Value") is not TextBlock text) return;
        double v = slider.Value;
        text.Text = slider.Name switch
        {
            nameof(VideoHueSlider) => $"{v:0}°",
            nameof(AudioDelaySlider) or nameof(SubtitleDelaySlider) => $"{v / 1000.0:+0.0;-0.0;0}s",
            nameof(EqualizerPreampSlider) => $"{v:+0.#;-0.#;0} dB",
            nameof(SubtitleSizeSlider) => $"{v:0}%",
            _ => $"{v * 100:0}%"
        };
    }

    private void OnSessionSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || VideoHueSlider == null || SubtitleSizeSlider == null) return;
        UpdateSliderValueText(slider);
        if (_syncingSession) return;
        var svc = ViewModel.PlaybackService;

        if (slider == VideoBrightnessSlider || slider == VideoContrastSlider || slider == VideoSaturationSlider ||
            slider == VideoGammaSlider || slider == VideoHueSlider)
        {
            svc.SetVideoAdjustments(new VideoAdjustments
            {
                Brightness = VideoBrightnessSlider.Value,
                Contrast = VideoContrastSlider.Value,
                Saturation = VideoSaturationSlider.Value,
                Gamma = VideoGammaSlider.Value,
                Hue = VideoHueSlider.Value
            });
            SaveVideoPrefsIfRemembered();
        }
        else if (slider == EqualizerPreampSlider)
        {
            ApplyEqualizerFromControls(markCustom: false);
        }
        else if (slider == AudioDelaySlider)
        {
            svc.SetAudioDelay((long)AudioDelaySlider.Value);
            SaveAudioPrefsIfRemembered();
        }
        else if (slider == SubtitleDelaySlider)
        {
            svc.SetSubtitleDelay((long)SubtitleDelaySlider.Value);
        }
        else if (slider == SubtitleSizeSlider)
        {
            OnSubtitleStyleChanged(slider, null!);
        }
    }

    private void OnSessionRepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSession || SessionRepeatCombo.SelectedIndex < 0) return;
        var target = (RepeatMode)SessionRepeatCombo.SelectedIndex;
        // Step the existing toggle so the toast and saved setting stay consistent
        for (int i = 0; i < 3 && ViewModel.RepeatMode != target; i++) ViewModel.ToggleRepeatMode();
    }

    private void OnLoopSetAClick(object sender, RoutedEventArgs e) { ViewModel.SetLoopPoint(isStart: true); UpdateLoopStatus(); }
    private void OnLoopSetBClick(object sender, RoutedEventArgs e) { ViewModel.SetLoopPoint(isStart: false); UpdateLoopStatus(); }
    private void OnLoopClearClick(object sender, RoutedEventArgs e) { ViewModel.ClearLoop(); UpdateLoopStatus(); }

    private void UpdateLoopStatus()
    {
        if (LoopStatusText == null) return;
        var loc = Loc;
        if (ViewModel.LoopStart is TimeSpan a)
        {
            string b = ViewModel.LoopEnd is TimeSpan end ? PlayerViewModel.FormatTime(end) : "…";
            LoopStatusText.Text = string.Format(loc["Player_Session_LoopActive"], PlayerViewModel.FormatTime(a), b);
        }
        else
        {
            LoopStatusText.Text = loc["Player_Session_LoopHint"];
        }
        LoopClearButton.IsEnabled = ViewModel.LoopStart.HasValue;
    }

    private void SetAspect(string? ratio)
    {
        ViewModel.PlaybackService.SetAspectRatio(ratio);
        ViewModel.ShowToast(Loc["Player_Session_Aspect"], ratio ?? Loc["Player_Aspect_Default"], OsdKind.Tracks);
        SaveVideoPrefsIfRemembered();
        _syncingSession = true;
        VideoAspectCombo.SelectedIndex = Math.Max(0, Array.FindIndex(AspectOptions(Loc), a => a.Value == ratio));
        _syncingSession = false;
    }

    private void OnVideoAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSession || VideoAspectCombo.SelectedIndex < 0) return;
        SetAspect(AspectOptions(Loc)[VideoAspectCombo.SelectedIndex].Value);
    }

    private void OnVideoResetClick(object sender, RoutedEventArgs e)
    {
        ViewModel.PlaybackService.SetVideoAdjustments(new VideoAdjustments());
        ViewModel.PlaybackService.SetAspectRatio(null);
        SaveVideoPrefsIfRemembered();
        SyncSessionControls();
    }

    private void OnSessionRememberToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSession) return;
        var prefs = Storage.CurrentSettings.PlayerSession;
        if (ReferenceEquals(sender, VideoRememberToggle))
        {
            prefs.RememberVideo = VideoRememberToggle.IsOn;
            SaveVideoPrefsIfRemembered(force: true);
        }
        else if (ReferenceEquals(sender, AudioRememberToggle))
        {
            prefs.RememberAudio = AudioRememberToggle.IsOn;
            SaveAudioPrefsIfRemembered(force: true);
        }
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void SaveVideoPrefsIfRemembered(bool force = false)
    {
        var prefs = Storage.CurrentSettings.PlayerSession;
        if (!prefs.RememberVideo && !force) return;
        prefs.Video = ViewModel.PlaybackService.VideoAdjust.Clone();
        prefs.AspectRatio = ViewModel.PlaybackService.AspectRatio;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void SaveAudioPrefsIfRemembered(bool force = false)
    {
        var prefs = Storage.CurrentSettings.PlayerSession;
        if (!prefs.RememberAudio && !force) return;
        prefs.AudioDelayMs = (long)AudioDelaySlider.Value;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    /// <summary>Remembered settings come back when the player starts.</summary>
    private void ApplyRememberedSession()
    {
        var prefs = Storage.CurrentSettings.PlayerSession;
        var svc = ViewModel.PlaybackService;
        if (prefs.RememberVideo)
        {
            svc.SetVideoAdjustments(prefs.Video);
            svc.SetAspectRatio(prefs.AspectRatio);
        }
        if (prefs.EqualizerEnabled) svc.SetEqualizer(prefs.EqualizerBands, prefs.EqualizerPreamp);
        if (prefs.RememberAudio && prefs.AudioDelayMs != 0) svc.SetAudioDelay(prefs.AudioDelayMs);
        svc.SetSubtitleStyle(prefs.Subtitles);
        if (!string.IsNullOrEmpty(prefs.AudioOutputDevice))
        {
            // Applied once a clip is playing (the device list belongs to the player)
            void Restore()
            {
                svc.PlaybackStarted -= Restore;
                DispatcherQueue.TryEnqueue(() => svc.SetAudioOutputDevice(prefs.AudioOutputDevice!));
            }
            svc.PlaybackStarted += Restore;
        }
    }

    // ---- Equalizer -----------------------------------------------------------------------------

    private void BuildEqualizerBands()
    {
        EqualizerBandsGrid.Children.Clear();
        EqualizerBandsGrid.ColumnDefinitions.Clear();
        _eqBandSliders.Clear();
        var freqs = ViewModel.PlaybackService.EqualizerBandFrequencies();
        if (freqs.Length == 0) freqs = new float[] { 60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000 };
        for (int i = 0; i < freqs.Length; i++)
        {
            EqualizerBandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -20,
                Maximum = 20,
                StepFrequency = 0.5,
                Height = 118,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsThumbToolTipEnabled = true
            };
            slider.ValueChanged += (s, e) =>
            {
                if (!_syncingSession) ApplyEqualizerFromControls(markCustom: true);
            };
            var label = new TextBlock
            {
                Text = freqs[i] >= 1000 ? $"{freqs[i] / 1000:0.#}k" : $"{freqs[i]:0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var cell = new Grid();
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.Children.Add(slider);
            Grid.SetRow(label, 1);
            cell.Children.Add(label);
            Grid.SetColumn(cell, i);
            EqualizerBandsGrid.Children.Add(cell);
            _eqBandSliders.Add(slider);
        }
    }

    private void SetEqualizerControlsEnabled(bool enabled)
    {
        EqualizerPresetCombo.IsEnabled = enabled;
        EqualizerPreampSlider.IsEnabled = enabled;
        foreach (var s in _eqBandSliders) s.IsEnabled = enabled;
        EqualizerBandsGrid.Opacity = enabled ? 1 : 0.45;
    }

    private void OnEqualizerToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSession) return;
        var prefs = Storage.CurrentSettings.PlayerSession;
        prefs.EqualizerEnabled = EqualizerToggle.IsOn;
        SetEqualizerControlsEnabled(prefs.EqualizerEnabled);
        if (prefs.EqualizerEnabled) ApplyEqualizerFromControls(markCustom: false);
        else ViewModel.PlaybackService.SetEqualizer(null, 0);
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
        ViewModel.ShowToast(Loc["Player_Session_Equalizer"], Loc[prefs.EqualizerEnabled ? "Player_On" : "Player_Off"], OsdKind.Tracks);
    }

    private void OnEqualizerPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSession || EqualizerPresetCombo.SelectedIndex < 1) return;
        int preset = EqualizerPresetCombo.SelectedIndex - 1;
        var (bands, preamp) = ViewModel.PlaybackService.EqualizerPreset(preset);
        _syncingSession = true;
        for (int i = 0; i < _eqBandSliders.Count && i < bands.Length; i++) _eqBandSliders[i].Value = bands[i];
        EqualizerPreampSlider.Value = preamp;
        _syncingSession = false;
        Storage.CurrentSettings.PlayerSession.EqualizerPreset = preset;
        ApplyEqualizerFromControls(markCustom: false);
    }

    private void ApplyEqualizerFromControls(bool markCustom)
    {
        var prefs = Storage.CurrentSettings.PlayerSession;
        prefs.EqualizerBands = _eqBandSliders.Select(s => (float)s.Value).ToArray();
        prefs.EqualizerPreamp = (float)EqualizerPreampSlider.Value;
        if (markCustom && prefs.EqualizerPreset >= 0)
        {
            prefs.EqualizerPreset = -1;
            _syncingSession = true;
            EqualizerPresetCombo.SelectedIndex = 0;
            _syncingSession = false;
        }
        if (prefs.EqualizerEnabled) ViewModel.PlaybackService.SetEqualizer(prefs.EqualizerBands, prefs.EqualizerPreamp);
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    // ---- Colours -------------------------------------------------------------------------------

    private static readonly (string Hex, string Name)[] MomentColors =
    {
        ("#FFD700", "Gold"), ("#4CC2FF", "Cyan"), ("#2ECC71", "Emerald"), ("#FF6B6B", "Coral"), ("#AF52DE", "Violet"), ("#FF9500", "Orange")
    };

    private void InitializeMomentColors()
    {
        var presets = new List<Controls.ColorChoice>();
        foreach (var (hex, name) in MomentColors)
        {
            presets.Add(new Controls.ColorChoice { Value = hex, Color = ParseColor(hex), Name = name });
        }
        MarkMomentColorChooser.Presets = presets;
        ConfigurePlayerChooser(MarkMomentColorChooser, FlyoutPlacementMode.Top);
        MarkMomentColorChooser.ColorChanged += (s, e) => _selectedMarkColor = e.Value;
    }

    /// <summary>Choosers inside the player sit on video glass and keep the controls from auto-hiding while their picker is open.</summary>
    private void ConfigurePlayerChooser(Controls.ColorChooser chooser, FlyoutPlacementMode placement)
    {
        chooser.FlyoutPlacement = placement;
        chooser.UseVideoGlass = true;
        chooser.FlyoutPresenterStyle = (Style)Resources["PlayerFlyoutPresenterStyle"];
        chooser.PickerPadding = new Thickness(14, 12, 14, 14);
        chooser.PickerOpened += (s, e) => OnFlyoutOpened(chooser, e);
        chooser.PickerClosed += (s, e) => OnFlyoutClosed(chooser, e);
    }

    // ---- Subtitles -----------------------------------------------------------------------------

    private static readonly (int Rgb, string Hex)[] SubtitleColors =
    {
        (0xFFFFFF, "#FFFFFF"), (0xFFE45C, "#FFE45C"), (0x7CF0FF, "#7CF0FF"), (0x9CFF7C, "#9CFF7C"), (0xFF9EC4, "#FF9EC4")
    };

    private void BuildSubtitleColors()
    {
        var presets = new List<Controls.ColorChoice>();
        foreach (var (_, hex) in SubtitleColors)
        {
            presets.Add(new Controls.ColorChoice { Value = hex, Color = ParseColor(hex), Name = hex });
        }
        SubtitleColorChooser.Presets = presets;
        ConfigurePlayerChooser(SubtitleColorChooser, FlyoutPlacementMode.Left);
        // The overlay previews the colour while the picker is dragged; the style is saved once it's chosen
        SubtitleColorChooser.ColorChanged += (s, e) => SubtitleOverlayText.Foreground = new SolidColorBrush(e.Color);
        SubtitleColorChooser.ColorCommitted += (s, e) =>
        {
            var style = ViewModel.PlaybackService.SubtitleStyle.Clone();
            style.ColorRgb = (e.Color.R << 16) | (e.Color.G << 8) | e.Color.B;
            CommitSubtitleStyle(style);
        };
    }

    private void UpdateSubtitleColorSelection()
    {
        int current = ViewModel.PlaybackService.SubtitleStyle.ColorRgb & 0xFFFFFF;
        SubtitleColorChooser.SelectedValue = $"#{current:X6}";
    }

    private void OnSubtitleStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSession) return;
        var style = ViewModel.PlaybackService.SubtitleStyle.Clone();
        style.Scale = SubtitleSizeSlider.Value / 100.0;
        if (SubtitleBackCombo.SelectedIndex >= 0) style.Background = (SubtitleBackground)SubtitleBackCombo.SelectedIndex;
        style.Bold = SubtitleBoldToggle.IsOn;
        CommitSubtitleStyle(style);
    }

    private void OnSubtitleStyleToggled(object sender, RoutedEventArgs e) => OnSubtitleStyleChanged(sender, null!);

    private void CommitSubtitleStyle(SubtitleStyle style)
    {
        ViewModel.PlaybackService.SetSubtitleStyle(style);
        Storage.CurrentSettings.PlayerSession.Subtitles = style.Clone();
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
        ApplySubtitleOverlayStyle();
    }
}
