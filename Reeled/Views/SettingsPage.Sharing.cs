using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Reeled.Helpers;
using Reeled.Models;
using Reeled.Services;
using Reeled.Services.Sharing;

namespace Reeled.Views;

/// <summary>
/// Settings, Sharing: FFmpeg, what the Share dialog starts with, compression and the PixelDrain key; plus the
/// Shared clips card under Storage (compressed copies and the share history).
/// </summary>
public sealed partial class SettingsPage
{
    // Discord: 20 MB free, 50 MB with Nitro Basic, 1 GB with Nitro
    private static readonly int[] ShareSizes = { 10, 20, 50, 100, 250, 500, 1024 };
    private static readonly string[] LitterboxExpiries = { "1h", "12h", "24h", "72h" };
    private static readonly ShareDestination[] ShareDestinations =
        { ShareDestination.Litterbox, ShareDestination.Catbox, ShareDestination.GoFile, ShareDestination.PixelDrain, ShareDestination.FileOnly };
    private static readonly ShareQuality[] ShareQualities = { ShareQuality.Compressed, ShareQuality.Original, ShareQuality.Custom };

    private bool _syncingShare;
    private bool _shareWatched;
    private bool _pixelDrainOpen;

    private SharePreferences SharePrefs => Storage.CurrentSettings.Share ??= new SharePreferences();

    /// <summary>Labels and list items in the current language. Cheap: nothing here touches the disk.</summary>
    private void LocalizeSharing(ILocalizationService loc)
    {
        if (SharingSectionTitle == null) return;
        SharingSectionTitle.Text = loc["Cat_Sharing"];
        SubSharingCompression.Text = loc["SettingsSubSharingCompression"];
        SubSharingUpload.Text = loc["SettingsSubSharingUpload"];
        FfmpegInstallButton.Content = loc["Share_FfmpegInstall"];
        FfmpegLocateButton.Content = loc["Share_FfmpegLocate"];
        FfmpegAutoButton.Content = loc["Sharing_FfmpegAuto"];
        ShareQualityTitleText.Text = loc["Share_Quality"];
        ShareQualitySubtitleText.Text = loc["Sharing_QualitySubtitle"];
        ShareSizeLabel.Text = loc["Sharing_SizeLabel"];
        ShareSizeHint.Text = loc["Sharing_SizeHint"];
        ShareGpuTitleText.Text = loc["Sharing_GpuTitle"];
        ShareDestinationTitleText.Text = loc["Share_Destination"];
        ShareExpiryLabel.Text = loc["Sharing_ExpiryLabel"];
        ShareCopyLinkLabel.Text = loc["Sharing_CopyLinkLabel"];
        PixelDrainTitleText.Text = loc["Sharing_PixelDrainTitle"];
        PixelDrainSubtitleText.Text = loc["Sharing_PixelDrainSubtitle"];
        PixelDrainKeyBox.Header = loc["Sharing_PixelDrainKey"];
        PixelDrainKeyBox.PlaceholderText = loc["Appearance_IconSgdbKeyPlaceholder"];
        PixelDrainKeyLink.Content = loc["Appearance_IconGetKey"];
        AutomationProperties.SetName(PixelDrainToggle, loc["Sharing_PixelDrainToggle"]);
        SharePrivacyText.Text = loc["Sharing_Privacy"];
        StorageSharedTitleText.Text = loc["Storage_SharedTitle"];
        StorageSharedSubtitleText.Text = loc["Storage_SharedSubtitle"];
        StorageSharedCopiesButton.Content = loc["Storage_SharedDeleteCopies"];
        StorageSharedHistoryButton.Content = loc["Storage_SharedClearHistory"];
        ShareCustomLabel.Text = loc["Sharing_CustomLabel"];
        ShareCustomHint.Text = loc["Sharing_CustomHint"];
        ShareCustomHeightCombo.Header = loc["Share_CustomResolution"];
        ShareCustomFpsCombo.Header = loc["Share_CustomFps"];
        ShareCustomBitrateBox.Header = loc["Share_CustomBitrate"];

        _syncingShare = true;
        var prefs = SharePrefs;
        ShareQualityCombo.ItemsSource = new[]
        {
            string.Format(loc["Share_QualityCompressed"], prefs.TargetSizeMb),
            loc["Share_QualityOriginal"],
            loc["Share_QualityCustom"]
        };
        ShareQualityCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareQualities, prefs.Quality));
        ShareSizeCombo.ItemsSource = ShareSizes.Select(mb => mb switch
        {
            20 => string.Format(loc["Sharing_SizeDiscord"], mb),
            50 => string.Format(loc["Sharing_SizeNitroBasic"], mb),
            1024 => loc["Sharing_SizeNitro"],
            _ => string.Format(loc["Sharing_SizeMb"], mb)
        }).ToArray();
        ShareSizeCombo.SelectedIndex = NearestSizeIndex(prefs.TargetSizeMb);
        ShareDestinationCombo.ItemsSource = ShareDestinations.Select(d => Helpers.ShareUi.DestinationName(d, prefs)).ToArray();
        ShareDestinationCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareDestinations, prefs.Destination));
        ShareExpiryCombo.ItemsSource = LitterboxExpiries.Select(e => string.Format(loc["Sharing_Hours"], e.TrimEnd('h'))).ToArray();
        ShareExpiryCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LitterboxExpiries, prefs.LitterboxExpiry));
        ShareCustomHeightCombo.ItemsSource = ShareUi.CustomHeights
            .Select(h => h == 0 ? loc["Share_CustomSameAsClip"] : $"{h}p").ToArray();
        ShareCustomHeightCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareUi.CustomHeights, prefs.CustomHeight));
        ShareCustomFpsCombo.ItemsSource = ShareUi.CustomFrameRates
            .Select(f => f == 0 ? loc["Share_CustomSameAsClip"] : $"{f} fps").ToArray();
        ShareCustomFpsCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareUi.CustomFrameRates, prefs.CustomFps));
        ShareCustomBitrateBox.NumberFormatter ??= new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 1, IntegerDigits = 1 };
        ShareCustomBitrateBox.Value = Math.Round(prefs.CustomVideoKbps / 1000.0, 1);
        ShareCustomPanel.Visibility = prefs.Quality == ShareQuality.Custom ? Visibility.Visible : Visibility.Collapsed;
        ShareCustomPanel.Opacity = 1;
        _syncingShare = false;

        SetPixelDrainRevealed(PixelDrainKeyBox.PasswordRevealMode == PasswordRevealMode.Visible);
        UpdateShareDestinationHint();
    }

    /// <summary>A size that's no longer offered (25 MB) shows as the closest one that is.</summary>
    private static int NearestSizeIndex(int mb)
    {
        int best = 0;
        for (int i = 1; i < ShareSizes.Length; i++)
        {
            if (Math.Abs(ShareSizes[i] - mb) < Math.Abs(ShareSizes[best] - mb)) best = i;
        }
        return best;
    }

    /// <summary>Puts the stored choices on the controls each time Settings opens; disk and FFmpeg checks come after.</summary>
    private void LoadSharing()
    {
        if (ShareGpuToggle == null) return;
        var prefs = SharePrefs;
        _syncingShare = true;
        ShareGpuToggle.IsOn = prefs.UseHardwareEncoder;
        ShareCopyLinkToggle.IsOn = prefs.CopyLinkAutomatically;
        PixelDrainKeyBox.Password = GameIconCredentials.Unprotect(prefs.ProtectedPixelDrainKey) ?? string.Empty;
        _syncingShare = false;
        SetPixelDrainRevealed(false);

        if (!_shareWatched)
        {
            _shareWatched = true;
            // An FFmpeg install finishing while Settings is open, and shares made or removed elsewhere
            FfmpegLocator.Changed += () => DispatcherQueue.TryEnqueue(() => _ = UpdateFfmpegStatusAsync());
            App.GetService<ShareService>().HistoryChanged += path => DispatcherQueue.TryEnqueue(() => _ = UpdateSharedStorageAsync());
        }

        // Once the page is on screen: these read the disk and run ffmpeg
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _ = UpdateFfmpegStatusAsync();
            _ = UpdateSharedStorageAsync();
        });
    }

    private async Task UpdateFfmpegStatusAsync()
    {
        if (FfmpegSubtitleText == null) return;
        var loc = ViewModel.Loc;
        // PATH and winget's folders are searched off the UI thread
        string? path = await Task.Run(() => FfmpegLocator.Find(refresh: true));
        bool chosen = !string.IsNullOrWhiteSpace(SharePrefs.FfmpegPath);
        FfmpegInstallButton.Visibility = path == null && FfmpegLocator.IsWingetAvailable ? Visibility.Visible : Visibility.Collapsed;
        FfmpegAutoButton.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        FfmpegStatusIcon.Glyph = path == null ? "" : "";
        FfmpegStatusIcon.Foreground = Helpers.ThemeBrushes.Get(path == null ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush", this);
        ShareGpuSubtitleText.Text = loc["Sharing_GpuSubtitle"];

        if (path == null)
        {
            FfmpegSubtitleText.Text = loc["Sharing_FfmpegMissing"];
            return;
        }

        FfmpegSubtitleText.Text = string.Format(loc["Sharing_FfmpegFound"], await FfmpegLocator.GetVersionAsync() ?? "?", path);
        if (SharePrefs.UseHardwareEncoder)
        {
            var encoder = await ClipCompressor.DetectEncoderAsync(true);
            ShareGpuSubtitleText.Text = encoder.IsHardware
                ? string.Format(loc["Sharing_GpuFound"], encoder.DisplayName)
                : loc["Sharing_GpuNone"];
        }
    }

    private async Task UpdateSharedStorageAsync()
    {
        if (StorageSharedStatsText == null) return;
        var loc = ViewModel.Loc;
        var (files, bytes) = await Task.Run(ShareService.CopyStats);
        int links = App.GetService<ShareService>().History.Count(r => r.Link != null);
        StorageSharedStatsText.Text = string.Format(loc["Storage_SharedStats"],
            links, files, (bytes / (1024.0 * 1024.0)).ToString("0.#", loc.CurrentCulture));
        StorageSharedCopiesButton.IsEnabled = files > 0;
        StorageSharedHistoryButton.IsEnabled = App.GetService<ShareService>().History.Count > 0;
    }

    private void UpdateShareDestinationHint()
    {
        if (ShareDestinationSubtitleText == null) return;
        var loc = ViewModel.Loc;
        var destination = ShareDestinations[Math.Max(0, ShareDestinationCombo.SelectedIndex)];
        var (title, facts, detail) = ShareUi.DestinationInfo(destination, SharePrefs);
        ShareDestinationSubtitleText.Text = $"{title} · {facts}. {detail}";
    }

    private void OnShareSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingShare || _isInitializing) return;
        var prefs = SharePrefs;
        if (ShareQualityCombo.SelectedIndex >= 0)
        {
            var quality = ShareQualities[ShareQualityCombo.SelectedIndex];
            if (quality != prefs.Quality && ReferenceEquals(sender, ShareQualityCombo))
            {
                Helpers.Motion.Reveal(ShareCustomPanel, quality == ShareQuality.Custom, null, SettingsScrollViewer, OnRevealScrolling);
            }
            prefs.Quality = quality;
        }
        if (ShareDestinationCombo.SelectedIndex >= 0) prefs.Destination = ShareDestinations[ShareDestinationCombo.SelectedIndex];
        if (ShareExpiryCombo.SelectedIndex >= 0) prefs.LitterboxExpiry = LitterboxExpiries[ShareExpiryCombo.SelectedIndex];
        bool sizeChanged = false;
        if (ShareSizeCombo.SelectedIndex >= 0 && prefs.TargetSizeMb != ShareSizes[ShareSizeCombo.SelectedIndex])
        {
            prefs.TargetSizeMb = ShareSizes[ShareSizeCombo.SelectedIndex];
            sizeChanged = true;
        }
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);

        // The quality list names the size and the destination list names the expiry: refresh their texts,
        // after this handler (a list can't be refilled from inside its own SelectionChanged)
        if (sizeChanged || ReferenceEquals(sender, ShareExpiryCombo))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LocalizeSharing(ViewModel.Loc);
                FitSettingsComboBoxes();
            });
        }
        else
        {
            UpdateShareDestinationHint();
        }
    }

    private void OnShareCustomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingShare || _isInitializing) return;
        var prefs = SharePrefs;
        if (ShareCustomHeightCombo.SelectedIndex >= 0) prefs.CustomHeight = ShareUi.CustomHeights[ShareCustomHeightCombo.SelectedIndex];
        if (ShareCustomFpsCombo.SelectedIndex >= 0) prefs.CustomFps = ShareUi.CustomFrameRates[ShareCustomFpsCombo.SelectedIndex];
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnShareCustomBitrateChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncingShare || _isInitializing) return;
        // A cleared box puts back the stored value
        if (double.IsNaN(sender.Value))
        {
            sender.Value = Math.Round(SharePrefs.CustomVideoKbps / 1000.0, 1);
            return;
        }
        SharePrefs.CustomVideoKbps = (int)Math.Round(Math.Clamp(sender.Value, 0.3, 100) * 1000);
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnShareToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingShare || ShareGpuToggle == null) return;
        var prefs = SharePrefs;
        bool gpuChanged = prefs.UseHardwareEncoder != ShareGpuToggle.IsOn;
        prefs.UseHardwareEncoder = ShareGpuToggle.IsOn;
        prefs.CopyLinkAutomatically = ShareCopyLinkToggle.IsOn;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
        if (gpuChanged) _ = UpdateFfmpegStatusAsync();
    }

    private async void OnFfmpegInstallClick(object sender, RoutedEventArgs e) =>
        await Helpers.ShareUi.InstallFfmpegAsync(XamlRoot, ActualTheme);

    private async void OnFfmpegLocateClick(object sender, RoutedEventArgs e)
    {
        await Helpers.ShareUi.LocateFfmpegAsync();
        await UpdateFfmpegStatusAsync();
    }

    private void OnFfmpegAutoClick(object sender, RoutedEventArgs e) => FfmpegLocator.SetChosenPath(null);

    // ---------------------------------------------------------------------------------------------
    // Storage: Shared clips
    // ---------------------------------------------------------------------------------------------

    private void OnDeleteSharedCopiesClick(object sender, RoutedEventArgs e)
    {
        var (_, bytes) = ShareService.CopyStats();
        App.GetService<ShareService>().DeleteAllCopies();
        ShowToast(string.Format(ViewModel.Loc["Storage_SharedCopiesDeleted"], (bytes / (1024.0 * 1024.0)).ToString("0.#", ViewModel.Loc.CurrentCulture)),
            "", false);
    }

    private async void OnClearShareHistoryClick(object sender, RoutedEventArgs e)
    {
        var loc = ViewModel.Loc;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme != ElementTheme.Default ? ActualTheme : App.CurrentTheme,
            Title = loc["Storage_SharedClearTitle"],
            Content = new TextBlock { Text = loc["Storage_SharedClearMessage"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = loc["Storage_SharedClearHistory"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowSafeAsync() != ContentDialogResult.Primary) return;
        App.GetService<ShareService>().ClearHistory();
        ShowToast(loc["Storage_SharedHistoryCleared"], "", false);
    }

    // ---------------------------------------------------------------------------------------------
    // PixelDrain key (folds open like Icon sources)
    // ---------------------------------------------------------------------------------------------

    private void OnPixelDrainToggleClick(object sender, RoutedEventArgs e)
    {
        _pixelDrainOpen = !_pixelDrainOpen;
        Helpers.Motion.Reveal(PixelDrainBody, _pixelDrainOpen, null, SettingsScrollViewer, OnRevealScrolling);
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(PixelDrainChevronRotation, "Angle", _pixelDrainOpen ? 180 : 0, Helpers.Motion.Medium));
        Helpers.Motion.BeginExact(sb);
    }

    private void CommitPixelDrainKey()
    {
        if (PixelDrainKeyBox == null || _syncingShare) return;
        var prefs = SharePrefs;
        string stored = GameIconCredentials.Unprotect(prefs.ProtectedPixelDrainKey) ?? string.Empty;
        string typed = PixelDrainKeyBox.Password.Trim();
        if (typed == stored) return;
        prefs.ProtectedPixelDrainKey = GameIconCredentials.Protect(typed);
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnPixelDrainKeyLostFocus(object sender, RoutedEventArgs e) => CommitPixelDrainKey();

    private void OnPixelDrainRevealClick(object sender, RoutedEventArgs e) =>
        SetPixelDrainRevealed(PixelDrainKeyBox.PasswordRevealMode != PasswordRevealMode.Visible);

    private void SetPixelDrainRevealed(bool revealed)
    {
        if (PixelDrainKeyBox == null) return;
        PixelDrainKeyBox.PasswordRevealMode = revealed ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        if (PixelDrainKeyReveal.Content is FontIcon icon) icon.Glyph = revealed ? "" : "";
        string label = ViewModel.Loc[revealed ? "Appearance_IconHideKey" : "Appearance_IconShowKey"];
        AutomationProperties.SetName(PixelDrainKeyReveal, label);
        ToolTipService.SetToolTip(PixelDrainKeyReveal, label);
    }
}
