using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

/// <summary>
/// Right-click menu on the video: everything the player can do, including what the bar hides
/// (VLC concept, built fresh each time so it always reflects the current state).
/// </summary>
public sealed partial class PlayerPage
{
    private Style? _playerMenuStyle;

    /// <summary>Menus over the video use a solid presenter: acrylic can't see the swap chain.</summary>
    private Style PlayerMenuStyle
    {
        get
        {
            if (_playerMenuStyle != null) return _playerMenuStyle;
            var style = new Style(typeof(MenuFlyoutPresenter)) { BasedOn = (Style)Application.Current.Resources["ReeledMenuFlyoutPresenterStyle"] };
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(244, 30, 30, 30))));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255))));
            style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, ElementTheme.Dark));
            return _playerMenuStyle = style;
        }
    }

    /// <summary>The Share dialog for the clip that's playing; a set A–B loop is offered as "only this part".</summary>
    private async void ShareCurrentClip()
    {
        var clip = ViewModel.CurrentClip;
        if (clip == null) return;
        (TimeSpan, TimeSpan)? range = ViewModel.LoopStart is TimeSpan a && ViewModel.LoopEnd is TimeSpan b && b > a
            ? (a, b - a)
            : null;
        await Helpers.ShareUi.ShowAsync(clip, XamlRoot, ElementTheme.Dark, range);
    }

    private void OnShareClick(object sender, RoutedEventArgs e) => ShareCurrentClip();

    private void OnVideoRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var menu = BuildPlayerContextMenu();
        menu.ShowAt(RootContainer, new FlyoutShowOptions { Position = e.GetPosition(RootContainer) });
        e.Handled = true;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action click, string? shortcut = null, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
        if (shortcut != null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (s, e) => click();
        return item;
    }

    private static RadioMenuFlyoutItem Radio(string text, string group, bool isChecked, Action click)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
        item.Click += (s, e) => click();
        return item;
    }

    private static ToggleMenuFlyoutItem Toggle(string text, string glyph, bool isChecked, Action click, string? shortcut = null)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsChecked = isChecked };
        if (shortcut != null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (s, e) => click();
        return item;
    }

    private MenuFlyout BuildPlayerContextMenu()
    {
        var loc = Loc;
        var vm = ViewModel;
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = PlayerMenuStyle };
        menu.Opened += (s, e) => { _openFlyoutsCount++; _inactivityTimer.Stop(); };
        menu.Closed += (s, e) =>
        {
            _openFlyoutsCount = Math.Max(0, _openFlyoutsCount - 1);
            if (vm.IsPlaying && !AreFlyoutsOrPopupsOpen) RestartInactivityTimer();
        };

        menu.Items.Add(Item(vm.IsPlaying ? loc["Player_Menu_Pause"] : loc["Player_Menu_Play"], vm.IsPlaying ? "" : "", vm.TogglePlayPause, "Space"));
        menu.Items.Add(Item(loc["Player_Tooltip_Stop"].Split('(')[0].Trim(), "", vm.Stop, "S"));
        menu.Items.Add(Item(loc["Player_Tooltip_PreviousClip"], "", vm.PlayPrevious, "P", vm.HasPreviousClip));
        menu.Items.Add(Item(loc["Player_Tooltip_NextClip"], "", vm.PlayNext, "N", vm.HasNextClip));
        menu.Items.Add(Item(loc["Player_MarkMoment_Title"], "", () => ShowMomentsFlyout(MomentsTabMark), "B"));
        menu.Items.Add(Item(loc["Player_Share"] + "…", "", ShareCurrentClip, enabled: vm.CurrentClip != null));
        menu.Items.Add(new MenuFlyoutSeparator());

        // Playback
        var playback = new MenuFlyoutSubItem { Text = loc["Player_Tab_Playback"], Icon = new FontIcon { Glyph = "" } };
        foreach (var speed in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            double sp = speed;
            playback.Items.Add(Radio(sp == 1.0 ? loc["Player_PresetNormal"] : $"{sp:0.##}x", "ctxSpeed", Math.Abs(vm.PlaybackRate - sp) < 0.01, () =>
            {
                vm.ChangeSpeed(sp);
                UpdateSpeedPresetHighlight();
            }));
        }
        playback.Items.Add(new MenuFlyoutSeparator());
        playback.Items.Add(Radio(loc["Player_Session_RepeatOff"], "ctxRepeat", vm.RepeatMode == RepeatMode.Off, () => SetRepeat(RepeatMode.Off)));
        playback.Items.Add(Radio(loc["Player_Session_RepeatAll"], "ctxRepeat", vm.RepeatMode == RepeatMode.All, () => SetRepeat(RepeatMode.All)));
        playback.Items.Add(Radio(loc["Player_Session_RepeatOne"], "ctxRepeat", vm.RepeatMode == RepeatMode.One, () => SetRepeat(RepeatMode.One)));
        playback.Items.Add(Toggle(loc["Player_Tooltip_Shuffle"], "", vm.IsShuffleEnabled, () => vm.IsShuffleEnabled = !vm.IsShuffleEnabled));
        playback.Items.Add(new MenuFlyoutSeparator());
        playback.Items.Add(Item(loc["Player_Session_LoopA"], "", () => { vm.SetLoopPoint(true); UpdateLoopStatus(); }, "L"));
        playback.Items.Add(Item(loc["Player_Session_LoopB"], "", () => { vm.SetLoopPoint(false); UpdateLoopStatus(); }, null, vm.LoopStart.HasValue));
        playback.Items.Add(Item(loc["Player_Session_LoopClear"], "", () => { vm.ClearLoop(); UpdateLoopStatus(); }, null, vm.LoopStart.HasValue));
        playback.Items.Add(new MenuFlyoutSeparator());
        playback.Items.Add(Item(loc["Player_Tooltip_FrameBack"].Split('(')[0].Trim(), "", () => vm.StepFrame(false), ","));
        playback.Items.Add(Item(loc["Player_Tooltip_FrameForward"].Split('(')[0].Trim(), "", () => vm.StepFrame(true), "."));
        menu.Items.Add(playback);

        // Video
        var video = new MenuFlyoutSubItem { Text = loc["Player_Tab_Video"], Icon = new FontIcon { Glyph = "" } };
        var aspect = new MenuFlyoutSubItem { Text = loc["Player_Session_Aspect"] };
        foreach (var (value, label) in AspectOptions(loc))
        {
            var v = value;
            aspect.Items.Add(Radio(label, "ctxAspect", vm.PlaybackService.AspectRatio == v, () => SetAspect(v)));
        }
        video.Items.Add(aspect);
        video.Items.Add(Item(loc["Player_Tooltip_Snapshot"], "", () => _ = vm.TakeSnapshotAsync()));
        video.Items.Add(Item(loc["Player_Tooltip_Fullscreen"].Split('(')[0].Trim(), "", ToggleFullscreen, "F"));
        video.Items.Add(Item(loc["Player_Tooltip_Pip"], "", TogglePictureInPicture));
        video.Items.Add(new MenuFlyoutSeparator());
        video.Items.Add(Item(loc["Player_Session_Picture"] + "…", "", () => OpenSessionPanel(SessionTabVideo)));
        menu.Items.Add(video);

        // Audio
        var audio = new MenuFlyoutSubItem { Text = loc["Player_Tab_Audio"], Icon = new FontIcon { Glyph = "" } };
        vm.RefreshAudioTracks();
        var active = vm.ActiveAudioTracks;
        foreach (var track in vm.AudioTracks)
        {
            int id = track.Id;
            audio.Items.Add(Toggle(track.DisplayName, "", active.Contains(id), () => vm.ToggleAudioTrack(id)));
        }
        audio.Items.Add(Toggle(loc["Player_Tooltip_MuteUnmute"].Split('(')[0].Trim(), "", vm.IsMuted, vm.ToggleMute, "M"));
        var devices = vm.PlaybackService.GetAudioOutputDevices();
        if (devices.Count > 0)
        {
            var output = new MenuFlyoutSubItem { Text = loc["Player_Tracks_OutputDevice"] };
            string? current = vm.PlaybackService.CurrentAudioOutputDevice;
            foreach (var (deviceId, name) in devices)
            {
                string idCopy = deviceId;
                output.Items.Add(Radio(name, "ctxDevice", string.Equals(current, deviceId, StringComparison.OrdinalIgnoreCase), () =>
                {
                    if (vm.PlaybackService.SetAudioOutputDevice(idCopy)) vm.ShowToast(name, kind: OsdKind.Tracks);
                }));
            }
            audio.Items.Add(output);
        }
        audio.Items.Add(new MenuFlyoutSeparator());
        audio.Items.Add(Item(loc["Player_Session_Equalizer"] + "…", "", () => OpenSessionPanel(SessionTabAudio)));
        menu.Items.Add(audio);

        // Subtitles
        var subs = new MenuFlyoutSubItem { Text = loc["Player_Tab_Subtitles"], Icon = new FontIcon { Glyph = "" } };
        vm.RefreshSubtitleTracks();
        subs.Items.Add(Radio(loc["Player_Tracks_None"], "ctxSubs", vm.SelectedSubtitleTrack == null, () => vm.SelectSubtitleTrack(-1)));
        foreach (var track in vm.SubtitleTracks)
        {
            int id = track.Id;
            subs.Items.Add(Radio(track.DisplayName, "ctxSubs", track.IsSelected, () => vm.SelectSubtitleTrack(id)));
        }
        subs.Items.Add(new MenuFlyoutSeparator());
        subs.Items.Add(Item(loc["Player_AddSubtitleFile"], "", async () =>
        {
            await vm.AddSubtitleFileAsync(WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        }));
        subs.Items.Add(Item(loc["Player_Session_Style"] + "…", "", () => OpenSessionPanel(SessionTabSubtitles)));
        menu.Items.Add(subs);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Clip
        menu.Items.Add(Item(loc["Player_ClipQueueTitle"], "", vm.ToggleSidebar, "Q"));
        if (vm.CurrentClip is GameClip clip)
        {
            menu.Items.Add(Item(loc["FolderMenu_RevealExplorer"], "", () => RevealInExplorer(clip.FilePath)));
            menu.Items.Add(Item(loc["Player_Menu_CopyPath"], "", () =>
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(clip.FilePath);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                vm.ShowToast(loc["Player_Menu_PathCopied"], kind: OsdKind.Tracks);
            }));

            // Tags belong to library clips; a file opened from disk has none to edit
            var home = App.GetService<HomeViewModel>();
            if (home.FindLibraryClip(clip.FilePath) is GameClip libraryClip)
            {
                menu.Items.Add(Helpers.TagUi.BuildClipSubmenu(home, libraryClip, XamlRoot, ElementTheme.Dark, "Player_Menu_Tags"));
            }
        }
        menu.Items.Add(Item(loc["Player_Menu_OpenMedia"], "", OpenMediaFromPlayer, "Ctrl+O"));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(loc["Player_Menu_HideControls"], "", () =>
        {
            _inactivityTimer.Stop();
            DispatcherQueue.TryEnqueue(() => vm.IsControlsVisible = false);
        }, "H"));
        menu.Items.Add(Item(loc["Toolbar_EditorTitle"], "", OpenToolbarEditor));
        menu.Items.Add(Item(loc["Player_Tooltip_Session"], "", () => OpenSessionPanel(SessionTabPlayback)));
        menu.Items.Add(Item(loc["Player_BackToLibrary"], "", vm.BackToHome, "Esc"));
        return menu;
    }

    private void SetRepeat(RepeatMode target)
    {
        for (int i = 0; i < 3 && ViewModel.RepeatMode != target; i++) ViewModel.ToggleRepeatMode();
    }

    /// <summary>Plays a file picked from disk (it joins the queue).</summary>
    private async void OpenMediaFromPlayer()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var ext in new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v", ".ts" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        ViewModel.AddFilesToPlaylist(new[] { file.Path });
        var clip = ViewModel.Playlist.FirstOrDefault(c => string.Equals(c.FilePath, file.Path, StringComparison.OrdinalIgnoreCase));
        if (clip != null) ViewModel.SelectPlaylistClip(clip);
    }
}
