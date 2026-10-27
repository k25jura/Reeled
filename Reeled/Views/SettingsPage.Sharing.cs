using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Reeled.Models;
using Reeled.Services;
using Reeled.Services.Sharing;

namespace Reeled.Views;

/// <summary>Settings, Sharing: FFmpeg, what the Share dialog starts with, compression and the PixelDrain key.</summary>
public sealed partial class SettingsPage
{
    private static readonly int[] ShareSizes = { 10, 20, 25, 50, 100 };
    private static readonly string[] LitterboxExpiries = { "1h", "12h", "24h", "72h" };
    private static readonly ShareDestination[] ShareDestinations =
        { ShareDestination.Litterbox, ShareDestination.Catbox, ShareDestination.GoFile, ShareDestination.PixelDrain, ShareDestination.FileOnly };

    private bool _syncingShare;
    private bool _ffmpegWatched;

    private SharePreferences SharePrefs => Storage.CurrentSettings.Share ??= new SharePreferences();

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
        ShareDestinationSubtitleText.Text = loc["Sharing_DestinationSubtitle"];
        ShareExpiryLabel.Text = loc["Sharing_ExpiryLabel"];
        ShareCopyLinkLabel.Text = loc["Sharing_CopyLinkLabel"];
        PixelDrainTitleText.Text = loc["Sharing_PixelDrainTitle"];
        PixelDrainSubtitleText.Text = loc["Sharing_PixelDrainSubtitle"];
        PixelDrainKeyBox.PlaceholderText = loc["Appearance_IconSgdbKeyPlaceholder"];
        PixelDrainKeyLink.Content = loc["Appearance_IconGetKey"];
        SharePrivacyText.Text = loc["Sharing_Privacy"];

        _syncingShare = true;
        var prefs = SharePrefs;
        ShareQualityCombo.ItemsSource = new[]
        {
            string.Format(loc["Share_QualityCompressed"], prefs.TargetSizeMb),
            loc["Share_QualityOriginal"]
        };
        ShareQualityCombo.SelectedIndex = prefs.Quality == ShareQuality.Original ? 1 : 0;
        ShareSizeCombo.ItemsSource = ShareSizes.Select(mb => mb switch
        {
            10 => string.Format(loc["Sharing_Size10"], mb),
            50 => string.Format(loc["Sharing_Size50"], mb),
            _ => string.Format(loc["Sharing_SizeMb"], mb)
        }).ToArray();
        ShareSizeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareSizes, prefs.TargetSizeMb));
        ShareDestinationCombo.ItemsSource = ShareDestinations.Select(d => Helpers.ShareUi.DestinationName(d, prefs)).ToArray();
        ShareDestinationCombo.SelectedIndex = Math.Max(0, Array.IndexOf(ShareDestinations, prefs.Destination));
        ShareExpiryCombo.ItemsSource = LitterboxExpiries.Select(e => string.Format(loc["Sharing_Hours"], e.TrimEnd('h'))).ToArray();
        ShareExpiryCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LitterboxExpiries, prefs.LitterboxExpiry));
        _syncingShare = false;

        SetPixelDrainRevealed(PixelDrainKeyBox.PasswordRevealMode == PasswordRevealMode.Visible);
        UpdateShareDestinationHint();
        _ = UpdateFfmpegStatusAsync();
    }

    /// <summary>Puts the stored choices on the controls (each time Settings opens).</summary>
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

        if (!_ffmpegWatched)
        {
            _ffmpegWatched = true;
            // An install started from here (or the Share dialog) finishing while Settings is open
            FfmpegLocator.Changed += () => DispatcherQueue.TryEnqueue(() => _ = UpdateFfmpegStatusAsync());
        }
        _ = UpdateFfmpegStatusAsync();
    }

    private async System.Threading.Tasks.Task UpdateFfmpegStatusAsync()
    {
        if (FfmpegSubtitleText == null) return;
        var loc = ViewModel.Loc;
        string? path = FfmpegLocator.Find(refresh: true);
        bool chosen = !string.IsNullOrWhiteSpace(SharePrefs.FfmpegPath);
        FfmpegInstallButton.Visibility = path == null && FfmpegLocator.IsWingetAvailable ? Visibility.Visible : Visibility.Collapsed;
        FfmpegAutoButton.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        FfmpegStatusIcon.Glyph = path == null ? "" : "";
        FfmpegStatusIcon.Foreground = Helpers.ThemeBrushes.Get(path == null ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush", this);

        if (path == null)
        {
            FfmpegSubtitleText.Text = loc["Sharing_FfmpegMissing"];
            ShareGpuSubtitleText.Text = loc["Sharing_GpuSubtitle"];
            return;
        }

        FfmpegSubtitleText.Text = string.Format(loc["Sharing_FfmpegFound"], await FfmpegLocator.GetVersionAsync() ?? "?", path);
        ShareGpuSubtitleText.Text = loc["Sharing_GpuSubtitle"];
        if (SharePrefs.UseHardwareEncoder)
        {
            var encoder = await ClipCompressor.DetectEncoderAsync(true);
            ShareGpuSubtitleText.Text = encoder.IsHardware
                ? string.Format(loc["Sharing_GpuFound"], encoder.DisplayName)
                : loc["Sharing_GpuNone"];
        }
    }

    private void UpdateShareDestinationHint()
    {
        if (ShareDestinationSubtitleText == null) return;
        var loc = ViewModel.Loc;
        var destination = ShareDestinations[Math.Max(0, ShareDestinationCombo.SelectedIndex)];
        ShareDestinationSubtitleText.Text = destination switch
        {
            ShareDestination.Litterbox => string.Format(loc["Share_HintLitterbox"], SharePrefs.LitterboxExpiry.Replace("h", " h")),
            ShareDestination.Catbox => loc["Share_HintCatbox"],
            ShareDestination.GoFile => loc["Share_HintGoFile"],
            ShareDestination.PixelDrain => loc["Share_HintPixelDrain"],
            _ => loc["Share_HintFileOnly"]
        };
    }

    private void OnShareSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingShare || _isInitializing) return;
        var prefs = SharePrefs;
        if (ShareQualityCombo.SelectedIndex >= 0) prefs.Quality = ShareQualityCombo.SelectedIndex == 1 ? ShareQuality.Original : ShareQuality.Compressed;
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

    private void CommitPixelDrainKey()
    {
        if (PixelDrainKeyBox == null || _syncingShare) return;
        var prefs = SharePrefs;
        string? stored = GameIconCredentials.Unprotect(prefs.ProtectedPixelDrainKey) ?? string.Empty;
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
