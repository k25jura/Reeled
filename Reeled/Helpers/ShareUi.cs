using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;
using Reeled.Services;
using Reeled.Services.Sharing;

namespace Reeled.Helpers;

/// <summary>
/// The Share dialog, the same from every place a clip can be shared (library menu, player title bar, player
/// menu). Picks the quality and where to upload (remembered for next time), explains what's missing
/// (FFmpeg, a PixelDrain key, a file too big for the host), shows the job's progress and then the link.
/// Closing it while a share runs keeps the job going; the result then arrives as a notification.
/// </summary>
internal static class ShareUi
{
    /// <summary>True while the dialog is on screen (finished jobs then report there, not as a notification).</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Opened when the user asks for the PixelDrain key or FFmpeg settings from the dialog.</summary>
    public static event Action? OpenSettingsRequested;

    private static ILocalizationService Loc => App.GetService<ILocalizationService>();

    /// <param name="range">The player's loop (start, length) when one is set, offered as "only this part".</param>
    public static async Task ShowAsync(GameClip clip, XamlRoot? root, ElementTheme theme, (TimeSpan Start, TimeSpan Length)? range = null)
    {
        if (root == null || IsOpen) return;
        var loc = Loc;
        var service = App.GetService<ShareService>();
        var storage = App.GetService<ILocalStorageService>();
        var prefs = storage.CurrentSettings.Share ??= new SharePreferences();

        // A share already running (maybe of another clip) is shown instead of starting a new one
        ShareJob? job = service.Current?.IsRunning == true ? service.Current : null;
        var subject = job?.Clip ?? clip;

        Brush Brush(string key) => ThemeBrushes.Get(key, theme);

        // ---- Clip header ----
        var thumb = new Border
        {
            Width = 112,
            Height = 63,
            CornerRadius = new CornerRadius(6),
            Background = Brush("ControlFillColorSecondaryBrush")
        };
        if (subject.Thumbnail != null) thumb.Child = new Image { Source = subject.Thumbnail, Stretch = Stretch.UniformToFill };
        else _ = LoadThumbAsync(thumb, subject);

        var name = new TextBlock
        {
            Text = subject.DisplayName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap
        };
        var facts = new TextBlock { FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush"), Text = Facts(subject) };
        var headerText = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(name);
        headerText.Children.Add(facts);
        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(thumb);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);

        // ---- Quality ----
        TextBlock Label(string text) => new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        Button Segment(string text) => new()
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 6, 10, 7),
            CornerRadius = new CornerRadius(4)
        };
        var compressedButton = Segment(string.Format(loc["Share_SegCompressed"], prefs.TargetSizeMb));
        var originalButton = Segment(loc["Share_SegOriginal"]);
        var customButton = Segment(loc["Share_SegCustom"]);
        var qualityRow = new Grid { ColumnSpacing = 6 };
        qualityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        qualityRow.ColumnDefinitions.Add(new ColumnDefinition());
        qualityRow.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(originalButton, 1);
        Grid.SetColumn(customButton, 2);
        qualityRow.Children.Add(compressedButton);
        qualityRow.Children.Add(originalButton);
        qualityRow.Children.Add(customButton);
        var qualityInfo = new InfoBlock(Brush);

        // ---- Custom quality: resolution, frame rate, bitrate (remembered) ----
        int[] heights = CustomHeights;
        int[] frameRates = CustomFrameRates;
        var heightBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = loc["Share_CustomResolution"] };
        foreach (var h in heights) heightBox.Items.Add(h == 0 ? loc["Share_CustomSameAsClip"] : $"{h}p");
        heightBox.SelectedIndex = Math.Max(0, Array.IndexOf(heights, prefs.CustomHeight));
        var fpsBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = loc["Share_CustomFps"] };
        foreach (var f in frameRates) fpsBox.Items.Add(f == 0 ? loc["Share_CustomSameAsClip"] : $"{f} fps");
        fpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(frameRates, prefs.CustomFps));
        var bitrateBox = new NumberBox
        {
            Header = loc["Share_CustomBitrate"],
            Minimum = 0.3,
            Maximum = 100,
            SmallChange = 0.5,
            LargeChange = 2,
            Value = Math.Round(prefs.CustomVideoKbps / 1000.0, 1),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 1, IntegerDigits = 1 }
        };
        var customPanel = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
        customPanel.ColumnDefinitions.Add(new ColumnDefinition());
        customPanel.ColumnDefinitions.Add(new ColumnDefinition());
        customPanel.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(fpsBox, 1);
        Grid.SetColumn(bitrateBox, 2);
        customPanel.Children.Add(heightBox);
        customPanel.Children.Add(fpsBox);
        customPanel.Children.Add(bitrateBox);

        // ---- Destination ----
        var destinations = new[] { ShareDestination.Litterbox, ShareDestination.Catbox, ShareDestination.GoFile, ShareDestination.PixelDrain, ShareDestination.FileOnly };
        var destinationBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var d in destinations) destinationBox.Items.Add(DestinationName(d, prefs));
        destinationBox.SelectedIndex = Math.Max(0, Array.IndexOf(destinations, prefs.Destination));
        var destinationInfo = new InfoBlock(Brush);

        // ---- Only the looped part ----
        CheckBox? rangeBox = null;
        if (range is { } r && job == null)
        {
            rangeBox = new CheckBox
            {
                Content = string.Format(loc["Share_OnlyLoop"], FormatTime(r.Start), FormatTime(r.Start + r.Length)),
                IsChecked = true
            };
        }

        // ---- Problems ----
        var notice = new InfoBar { IsClosable = false, IsOpen = false };
        var noticeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        notice.Content = noticeButtons;

        // ---- Progress and result ----
        var status = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, IsIndeterminate = true };
        var progressPanel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        progressPanel.Children.Add(status);
        progressPanel.Children.Add(bar);

        var linkBox = new TextBox { IsReadOnly = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        linkBox.GotFocus += (s, e) => linkBox.SelectAll();
        var copyLink = new Button { Content = loc["Share_CopyLink"] };
        var openLink = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(10, 7, 10, 7) };
        ToolTipService.SetToolTip(openLink, loc["Share_OpenLink"]);
        var linkRow = new Grid { ColumnSpacing = 6 };
        linkRow.ColumnDefinitions.Add(new ColumnDefinition());
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(copyLink, 1);
        Grid.SetColumn(openLink, 2);
        linkRow.Children.Add(linkBox);
        linkRow.Children.Add(copyLink);
        linkRow.Children.Add(openLink);
        var copyFile = new HyperlinkButton { Content = loc["Share_CopyVideo"], Padding = new Thickness(0) };
        var showFile = new HyperlinkButton { Content = loc["Share_ShowFile"], Padding = new Thickness(0) };
        var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        fileRow.Children.Add(copyFile);
        fileRow.Children.Add(showFile);
        var resultIcon = new FontIcon { Glyph = "", FontSize = 16, Foreground = Brush("AccentTextFillColorPrimaryBrush") };
        var resultText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var resultHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        resultHead.Children.Add(resultIcon);
        resultHead.Children.Add(resultText);
        var resultPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        resultPanel.Children.Add(resultHead);
        resultPanel.Children.Add(linkRow);
        resultPanel.Children.Add(fileRow);

        var options = new StackPanel { Spacing = 8 };
        options.Children.Add(Label(loc["Share_Quality"]));
        options.Children.Add(qualityRow);
        options.Children.Add(customPanel);
        options.Children.Add(qualityInfo.Root);
        options.Children.Add(Label(loc["Share_Destination"]));
        options.Children.Add(destinationBox);
        options.Children.Add(destinationInfo.Root);
        if (rangeBox != null) options.Children.Add(rangeBox);

        var panel = new StackPanel { Spacing = 14, Width = 440, RequestedTheme = theme };
        panel.Children.Add(header);
        panel.Children.Add(options);
        panel.Children.Add(notice);
        panel.Children.Add(progressPanel);
        panel.Children.Add(resultPanel);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = loc["Share_Title"],
            Content = panel,
            PrimaryButtonText = loc["Share_Button"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = ContentDialogButton.Primary
        };

        ShareQuality quality = prefs.Quality;
        ShareDestination Destination() => destinations[Math.Max(0, destinationBox.SelectedIndex)];
        bool UseRange() => rangeBox?.IsChecked == true;

        // ---- State ----
        void ShowNotice(InfoBarSeverity severity, string title, string message, params (string Text, Action Click)[] actions)
        {
            notice.Severity = severity;
            notice.Title = title;
            notice.Message = message;
            noticeButtons.Children.Clear();
            foreach (var (text, click) in actions)
            {
                var b = new Button { Content = text };
                b.Click += (s, e) => click();
                noticeButtons.Children.Add(b);
            }
            noticeButtons.Visibility = actions.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            notice.IsOpen = true;
        }

        void Refresh()
        {
            if (job != null) return;
            SegmentedButtons.Select(quality switch
            {
                ShareQuality.Original => originalButton,
                ShareQuality.Custom => customButton,
                _ => compressedButton
            }, compressedButton, originalButton, customButton);
            customPanel.Visibility = quality == ShareQuality.Custom ? Visibility.Visible : Visibility.Collapsed;
            var destination = Destination();
            double seconds = UseRange() && range is { } rr ? rr.Length.TotalSeconds : subject.Duration.TotalSeconds;
            long originalBytes = UseRange() && subject.Duration.TotalSeconds > 0
                ? (long)(subject.FileSizeBytes * seconds / subject.Duration.TotalSeconds)
                : subject.FileSizeBytes;
            long target = prefs.TargetSizeMb * 1024L * 1024L;
            // A whole clip that already fits is sent as it is (see ShareService), so FFmpeg isn't needed for it
            bool alreadySmall = !UseRange() && originalBytes > 0 && originalBytes <= target;
            bool needsFfmpeg = ShareService.NeedsFfmpeg(quality, UseRange()) && !(quality == ShareQuality.Compressed && alreadySmall);
            bool hasFfmpeg = FfmpegLocator.IsAvailable;
            long customBytes = ClipCompressor.EstimateCustomBytes(TimeSpan.FromSeconds(seconds), CustomKbps(subject, prefs));
            switch (quality)
            {
                case ShareQuality.Compressed when !alreadySmall:
                    qualityInfo.Set($"≤ {prefs.TargetSizeMb} {loc["Unit_MB"]}", PlanText(seconds, target, subject),
                        string.Format(loc["Share_InfoCompressed"], prefs.TargetSizeMb));
                    break;
                case ShareQuality.Compressed:
                    qualityInfo.Set(Mb(originalBytes), SourceText(subject), loc["Share_InfoAlreadySmall"]);
                    break;
                case ShareQuality.Custom:
                    qualityInfo.Set($"≈ {Mb(customBytes)}", CustomText(subject, prefs), loc["Share_InfoCustom"]);
                    break;
                default:
                    qualityInfo.Set(Mb(originalBytes), SourceText(subject), loc["Share_InfoOriginal"]);
                    break;
            }

            long uploadBytes = quality switch
            {
                ShareQuality.Compressed => Math.Min(target, Math.Max(originalBytes, 1)),
                ShareQuality.Custom => customBytes,
                _ => originalBytes
            };
            var (hostTitle, hostFacts, hostDetail) = DestinationInfo(destination, prefs);
            destinationInfo.Set(hostTitle, hostFacts, hostDetail);

            bool blocked = false;
            notice.IsOpen = false;
            if (needsFfmpeg && !hasFfmpeg)
            {
                blocked = true;
                var actions = new System.Collections.Generic.List<(string, Action)>();
                if (FfmpegLocator.IsWingetAvailable) actions.Add((loc["Share_FfmpegInstall"], () => _ = InstallFfmpegAsync(root, theme, dialog)));
                actions.Add((loc["Share_FfmpegLocate"], () => _ = LocateFfmpegAsync()));
                actions.Add((loc["Share_FfmpegDownload"], () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(FfmpegLocator.DownloadPage))));
                ShowNotice(InfoBarSeverity.Informational, loc["Share_FfmpegTitle"],
                    loc[quality == ShareQuality.Compressed ? "Share_FfmpegMessage" : "Share_FfmpegMessageTrim"], actions.ToArray());
            }
            else if (destination == ShareDestination.PixelDrain &&
                     string.IsNullOrWhiteSpace(GameIconCredentials.Unprotect(prefs.ProtectedPixelDrainKey)))
            {
                blocked = true;
                ShowNotice(InfoBarSeverity.Informational, loc["Share_PixelDrainTitle"], loc["Share_PixelDrainMessage"],
                    (loc["Share_OpenSettings"], () =>
                    {
                        dialog.Hide();
                        OpenSettingsRequested?.Invoke();
                    }));
            }
            else if (ShareUploader.MaxBytes(destination) is long max && uploadBytes > max)
            {
                blocked = true;
                ShowNotice(InfoBarSeverity.Warning, loc["Share_TooBigTitle"],
                    string.Format(loc["Share_TooBigMessage"], ShareUploader.HostName(destination), max / (1024 * 1024)));
            }
            dialog.IsPrimaryButtonEnabled = !blocked;
        }

        void ShowJob()
        {
            if (job == null) return;
            options.Visibility = Visibility.Collapsed;
            notice.IsOpen = false;
            bool running = job.IsRunning;
            bool done = job.Stage == ShareStage.Done;
            progressPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            resultPanel.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            status.Text = job.Status;
            bar.IsIndeterminate = job.Progress <= 0 || job.Stage == ShareStage.Preparing;
            bar.Value = job.Progress;

            dialog.PrimaryButtonText = string.Empty;
            dialog.SecondaryButtonText = running ? loc["Share_CancelJob"] : string.Empty;
            dialog.CloseButtonText = loc[running ? "Share_Hide" : "Share_Close"];
            if (running) return;

            bool hasLink = !string.IsNullOrEmpty(job.Link);
            linkRow.Visibility = hasLink ? Visibility.Visible : Visibility.Collapsed;
            linkBox.Text = job.Link ?? string.Empty;
            bool hasFile = job.OutputPath != null && File.Exists(job.OutputPath);
            fileRow.Visibility = done && hasFile ? Visibility.Visible : Visibility.Collapsed;
            copyFile.Visibility = job.FileCopied ? Visibility.Collapsed : Visibility.Visible;
            resultIcon.Glyph = done ? "" : job.Stage == ShareStage.Cancelled ? "" : "";
            resultIcon.Foreground = Brush(done ? "AccentTextFillColorPrimaryBrush" : job.Stage == ShareStage.Failed ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
            resultText.Text = done && job.OutputBytes > 0 && job.Request.Quality != ShareQuality.Original
                ? $"{job.Status}  ·  {Mb(job.OutputBytes)}"
                : job.Status;
            // A failed or cancelled share can be tried again with other choices
            if (!done)
            {
                dialog.PrimaryButtonText = loc["Share_TryAgain"];
            }
        }

        void Attach(ShareJob j)
        {
            job = j;
            j.PropertyChanged += OnJobChanged;
            ShowJob();
        }

        void OnJobChanged(object? sender, PropertyChangedEventArgs e) => dialog.DispatcherQueue.TryEnqueue(ShowJob);

        compressedButton.Click += (s, e) => { quality = ShareQuality.Compressed; Refresh(); };
        originalButton.Click += (s, e) => { quality = ShareQuality.Original; Refresh(); };
        customButton.Click += (s, e) => { quality = ShareQuality.Custom; Refresh(); };
        void SaveCustom()
        {
            prefs.CustomHeight = heights[Math.Max(0, heightBox.SelectedIndex)];
            prefs.CustomFps = frameRates[Math.Max(0, fpsBox.SelectedIndex)];
            if (!double.IsNaN(bitrateBox.Value)) prefs.CustomVideoKbps = (int)Math.Round(Math.Clamp(bitrateBox.Value, 0.3, 100) * 1000);
            _ = storage.SaveSettingsAsync(storage.CurrentSettings);
            Refresh();
        }
        heightBox.SelectionChanged += (s, e) => SaveCustom();
        fpsBox.SelectionChanged += (s, e) => SaveCustom();
        bitrateBox.ValueChanged += (s, e) => SaveCustom();
        destinationBox.SelectionChanged += (s, e) => Refresh();
        if (rangeBox != null) rangeBox.Click += (s, e) => Refresh();
        copyLink.Click += (s, e) =>
        {
            if (job?.Link != null && ShareService.CopyText(job.Link)) copyLink.Content = loc["Share_Copied"];
        };
        openLink.Click += (s, e) =>
        {
            if (job?.Link != null) _ = Windows.System.Launcher.LaunchUriAsync(new Uri(job.Link));
        };
        copyFile.Click += async (s, e) =>
        {
            if (job?.OutputPath != null && await ShareService.CopyFileAsync(job.OutputPath)) copyFile.Content = loc["Share_VideoCopied"];
        };
        showFile.Click += (s, e) =>
        {
            if (job?.OutputPath != null)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\""); } catch { }
            }
        };

        void OnFfmpegChanged() => dialog.DispatcherQueue.TryEnqueue(Refresh);
        FfmpegLocator.Changed += OnFfmpegChanged;

        dialog.PrimaryButtonClick += (s, e) =>
        {
            // Keep the dialog open: it turns into the progress view
            e.Cancel = true;
            if (job != null && job.IsRunning) return;
            if (job != null)
            {
                // Try again: back to the choices
                job.PropertyChanged -= OnJobChanged;
                job = null;
                options.Visibility = Visibility.Visible;
                progressPanel.Visibility = resultPanel.Visibility = Visibility.Collapsed;
                dialog.PrimaryButtonText = loc["Share_Button"];
                dialog.SecondaryButtonText = string.Empty;
                dialog.CloseButtonText = loc["Common_Cancel"];
                Refresh();
                return;
            }

            prefs.Quality = quality;
            prefs.Destination = Destination();
            _ = storage.SaveSettingsAsync(storage.CurrentSettings);
            var request = UseRange() && range is { } part
                ? new ShareRequest(clip, quality, Destination(), part.Start, part.Length)
                : new ShareRequest(clip, quality, Destination());
            Attach(service.Start(request));
        };
        dialog.SecondaryButtonClick += (s, e) =>
        {
            e.Cancel = true;
            service.Cancel();
        };

        if (job != null) Attach(job);
        else Refresh();

        IsOpen = true;
        try
        {
            await dialog.ShowSafeAsync();
        }
        finally
        {
            IsOpen = false;
            FfmpegLocator.Changed -= OnFfmpegChanged;
            if (job != null) job.PropertyChanged -= OnJobChanged;
        }
    }

    /// <summary>
    /// A clip's shares (the Shared badge and the clip menu open it): each link with its host, date and when it
    /// expires, the compressed copy it made, and what can be done with them. "Share again" opens the Share dialog.
    /// </summary>
    public static async Task ShowHistoryAsync(GameClip clip, XamlRoot? root, ElementTheme theme, Action? play = null)
    {
        if (root == null || IsOpen) return;
        var loc = Loc;
        var service = App.GetService<ShareService>();
        Brush Brush(string key) => ThemeBrushes.Get(key, theme);
        var subtle = (Style)Application.Current.Resources["SubtleButtonStyle"];

        var list = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };

        // ---- The original ----
        var thumb = new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(6), Background = Brush("ControlFillColorSecondaryBrush") };
        if (clip.Thumbnail != null) thumb.Child = new Image { Source = clip.Thumbnail, Stretch = Stretch.UniformToFill };
        else _ = LoadThumbAsync(thumb, clip);
        var name = new TextBlock { Text = clip.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var facts = new TextBlock { FontSize = 12, Text = Facts(clip), Foreground = Brush("TextFillColorSecondaryBrush") };
        bool originalExists = File.Exists(clip.FilePath);
        var playButton = new HyperlinkButton { Content = loc["Share_History_Play"], Padding = new Thickness(0), IsEnabled = originalExists && play != null };
        var revealButton = new HyperlinkButton { Content = loc["Share_ShowOriginal"], Padding = new Thickness(0), IsEnabled = originalExists };
        var originalLinks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        originalLinks.Children.Add(playButton);
        originalLinks.Children.Add(revealButton);
        var headerText = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(name);
        headerText.Children.Add(facts);
        headerText.Children.Add(originalLinks);
        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(thumb);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);

        var panel = new StackPanel { Spacing = 14, Width = 460, RequestedTheme = theme };
        panel.Children.Add(header);
        panel.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = loc["Share_History_Title"],
            Content = panel,
            PrimaryButtonText = loc["Share_History_ShareAgain"],
            CloseButtonText = loc["Share_Close"],
            DefaultButton = ContentDialogButton.Close
        };

        Button IconButton(string glyph, string tip, Action click, bool enabled = true)
        {
            var b = new Button
            {
                Style = subtle,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                IsEnabled = enabled,
                Content = new FontIcon { Glyph = glyph, FontSize = 13 }
            };
            ToolTipService.SetToolTip(b, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (s, e) => click();
            return b;
        }

        void Rebuild()
        {
            list.Children.Clear();
            var shares = service.SharesOf(clip.FilePath);
            if (shares.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = loc["Share_History_Empty"], Foreground = Brush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var share in shares) list.Children.Add(BuildRow(share));
        }

        FrameworkElement BuildRow(SharedLink share)
        {
            bool expired = share.ExpiresUtc is DateTime until && until <= DateTime.UtcNow;
            bool hasCopy = share.CopyPath != null && File.Exists(share.CopyPath);

            var title = new TextBlock
            {
                Text = share.Link != null ? ShareUploader.HostName(share.Destination) : loc["Share_History_CopiedOnly"],
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            };
            var details = new TextBlock
            {
                Text = string.Join("  ·  ", new[] { WhenText(share.SharedUtc), QualityText(share), RangeText(share) }.Where(t => !string.IsNullOrEmpty(t))),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("TextFillColorSecondaryBrush")
            };
            var expiry = new TextBlock
            {
                Text = ExpiryText(share),
                FontSize = 12,
                Foreground = Brush(expired ? "SystemFillColorCriticalBrush" : "TextFillColorTertiaryBrush")
            };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(title);
            text.Children.Add(details);
            if (share.Link != null)
            {
                var link = new HyperlinkButton
                {
                    Content = new TextBlock { Text = share.Link, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 },
                    Padding = new Thickness(0),
                    NavigateUri = expired ? null : new Uri(share.Link),
                    IsEnabled = !expired,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                text.Children.Add(link);
                text.Children.Add(expiry);
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
            if (share.Link != null)
            {
                Button? copyLink = null;
                copyLink = IconButton("", loc["Share_CopyLink"], () =>
                {
                    if (ShareService.CopyText(share.Link) && copyLink != null) ToolTipService.SetToolTip(copyLink, loc["Share_Copied"]);
                }, !expired);
                actions.Children.Add(copyLink);
            }
            actions.Children.Add(IconButton("", hasCopy ? loc["Share_CopyVideo"] : loc["Share_History_NoCopy"], async () =>
            {
                if (share.CopyPath != null) await ShareService.CopyFileAsync(share.CopyPath);
            }, hasCopy));
            actions.Children.Add(IconButton("", loc["Share_ShowFile"], () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{share.CopyPath}\""); } catch { }
            }, hasCopy));
            actions.Children.Add(IconButton("", loc["Share_History_Remove"], () =>
            {
                service.Remove(share);
                Rebuild();
            }));

            var row = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(12, 10, 6, 10),
                CornerRadius = new CornerRadius(6),
                Background = Brush("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = Brush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new FontIcon
            {
                Glyph = share.Link != null ? "" : "",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = Brush(expired ? "TextFillColorTertiaryBrush" : "AccentTextFillColorPrimaryBrush")
            };
            row.Children.Add(icon);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            return row;
        }

        playButton.Click += (s, e) =>
        {
            dialog.Hide();
            play?.Invoke();
        };
        revealButton.Click += (s, e) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{clip.FilePath}\""); } catch { }
        };

        Rebuild();
        bool shareAgain = false;
        dialog.PrimaryButtonClick += (s, e) => shareAgain = true;

        IsOpen = true;
        try
        {
            await dialog.ShowSafeAsync();
        }
        finally
        {
            IsOpen = false;
        }
        if (shareAgain && originalExists) await ShowAsync(clip, root, theme);
    }

    private static string WhenText(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var culture = App.GetService<ILocalizationService>().CurrentCulture;
        return local.Date == DateTime.Today
            ? string.Format(Loc["Share_History_Today"], local.ToString("t", culture))
            : local.ToString("d MMM, HH:mm", culture);
    }

    private static string QualityText(SharedLink share)
    {
        string kind = share.Quality switch
        {
            ShareQuality.Original => Loc["Share_QualityOriginal"],
            ShareQuality.Custom => Loc["Share_QualityCustom"],
            _ => Loc["Share_History_Compressed"]
        };
        return share.Bytes > 0 ? $"{kind} {Mb(share.Bytes)}" : kind;
    }

    private static string RangeText(SharedLink share) =>
        share.StartMs is long s && share.LengthMs is long l
            ? $"{FormatTime(TimeSpan.FromMilliseconds(s))} → {FormatTime(TimeSpan.FromMilliseconds(s + l))}"
            : string.Empty;

    /// <summary>"Expires in 2 h 10 min", "Expired", or what the host does with the file.</summary>
    private static string ExpiryText(SharedLink share)
    {
        var loc = Loc;
        if (share.ExpiresUtc is DateTime until)
        {
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return loc["Share_History_Expired"];
            string span = left.TotalHours >= 1
                ? string.Format(loc["Share_History_HoursMinutes"], (int)left.TotalHours, left.Minutes)
                : string.Format(loc["Share_History_Minutes"], Math.Max(1, left.Minutes));
            return string.Format(loc["Share_History_ExpiresIn"], span);
        }
        return share.Destination switch
        {
            ShareDestination.GoFile => loc["Share_History_GoFileKeeps"],
            ShareDestination.PixelDrain => loc["Share_History_PixelDrainKeeps"],
            _ => loc["Share_History_Permanent"]
        };
    }

    private static async Task LoadThumbAsync(Border host, GameClip clip)
    {
        try
        {
            var image = await App.GetService<IThumbnailService>().GetThumbnailAsync(clip.FilePath, highPriority: true);
            if (image != null) host.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
        }
        catch { }
    }

    /// <summary>Asks before running winget (it installs software), then opens PowerShell for it.</summary>
    public static async Task InstallFfmpegAsync(XamlRoot? root, ElementTheme theme, ContentDialog? hostDialog = null)
    {
        var loc = Loc;
        if (hostDialog != null)
        {
            // Two ContentDialogs can't be open at once; the share dialog says enough already
            if (!FfmpegLocator.InstallWithWinget()) await Windows.System.Launcher.LaunchUriAsync(new Uri(FfmpegLocator.DownloadPage));
            return;
        }
        if (root == null) return;
        var confirm = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = loc["Share_FfmpegInstallTitle"],
            Content = new TextBlock { Text = loc["Share_FfmpegInstallMessage"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = loc["Share_FfmpegInstall"],
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = ContentDialogButton.Primary
        };
        if (await confirm.ShowSafeAsync() != ContentDialogResult.Primary) return;
        if (!FfmpegLocator.InstallWithWinget()) await Windows.System.Launcher.LaunchUriAsync(new Uri(FfmpegLocator.DownloadPage));
    }

    /// <summary>Lets the user point at an ffmpeg.exe they already have.</summary>
    public static async Task LocateFfmpegAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file != null && string.Equals(file.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                FfmpegLocator.SetChosenPath(file.Path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Share.LocateFfmpeg", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Texts
    // ---------------------------------------------------------------------------------------------

    private static string Mb(long bytes) => $"{(bytes / (1024.0 * 1024.0)).ToString("0.#", Loc.CurrentCulture)} {Loc["Unit_MB"]}";

    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    private static string Facts(GameClip clip)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (clip.Duration > TimeSpan.Zero) parts.Add(FormatTime(clip.Duration));
        if (clip.FileSizeBytes > 0) parts.Add(Mb(clip.FileSizeBytes));
        if (clip.VideoHeight > 0)
        {
            string res = $"{clip.VideoWidth}×{clip.VideoHeight}";
            if (clip.Framerate > 0) res += $" · {ClipMetadataService.FormatFramerate(clip.Framerate)} fps";
            parts.Add(res);
        }
        return string.Join("  ·  ", parts);
    }

    /// <summary>"720p · 60 fps": what the size limit buys for a clip this long.</summary>
    private static string PlanText(double seconds, long target, GameClip clip)
    {
        var plan = ClipCompressor.PlanFor(TimeSpan.FromSeconds(seconds), target, (int)clip.VideoHeight);
        bool cap = plan.FpsCap > 0 && !(clip.Framerate is > 0 and <= 31);
        int fps = cap ? plan.FpsCap : (int)Math.Round(clip.Framerate > 0 ? clip.Framerate : 60);
        return $"{plan.MaxHeight}p · {fps} fps";
    }

    /// <summary>"1200p · 60 fps": the clip as it is.</summary>
    private static string SourceText(GameClip clip)
    {
        if (clip.VideoHeight <= 0) return string.Empty;
        string text = $"{clip.VideoHeight}p";
        if (clip.Framerate > 0) text += $" · {ClipMetadataService.FormatFramerate(clip.Framerate)} fps";
        return text;
    }

    /// <summary>"1080p · 60 fps · 8 Mbit/s": the custom settings as they apply to this clip (they only scale down).</summary>
    private static string CustomText(GameClip clip, SharePreferences prefs)
    {
        int height = prefs.CustomHeight > 0 && (clip.VideoHeight <= 0 || prefs.CustomHeight < clip.VideoHeight)
            ? prefs.CustomHeight : (int)clip.VideoHeight;
        double fps = prefs.CustomFps > 0 && (clip.Framerate <= 0 || prefs.CustomFps < clip.Framerate)
            ? prefs.CustomFps : clip.Framerate;
        var parts = new System.Collections.Generic.List<string>();
        if (height > 0) parts.Add($"{height}p");
        if (fps > 0) parts.Add($"{ClipMetadataService.FormatFramerate(fps)} fps");
        parts.Add(string.Format(Loc["Share_InfoBitrate"], (CustomKbps(clip, prefs) / 1000.0).ToString("0.#", Loc.CurrentCulture)));
        return string.Join(" · ", parts);
    }

    private static int CustomKbps(GameClip clip, SharePreferences prefs) =>
        ClipCompressor.CustomKbps(clip.VideoWidth, clip.VideoHeight, clip.Framerate, prefs.CustomHeight, prefs.CustomFps, prefs.CustomVideoKbps);

    /// <summary>"72 h" / "72 год": how long Litterbox keeps a file.</summary>
    public static string HoursText(string expiry) => string.Format(Loc["Sharing_Hours"], expiry.TrimEnd('h'));

    public static string DestinationName(ShareDestination destination, SharePreferences prefs) => destination switch
    {
        ShareDestination.Litterbox => string.Format(Loc["Share_DestLitterbox"], HoursText(prefs.LitterboxExpiry)),
        ShareDestination.Catbox => Loc["Share_DestCatbox"],
        ShareDestination.GoFile => Loc["Share_DestGoFile"],
        ShareDestination.PixelDrain => Loc["Share_DestPixelDrain"],
        _ => Loc["Share_DestFileOnly"]
    };

    /// <summary>What happens to a clip sent there: the main fact, the limits, and a sentence about the link.</summary>
    public static (string Title, string Facts, string Detail) DestinationInfo(ShareDestination destination, SharePreferences prefs) => destination switch
    {
        ShareDestination.Litterbox => (string.Format(Loc["Share_InfoKeptFor"], HoursText(prefs.LitterboxExpiry)),
            string.Format(Loc["Share_InfoAnonymousUpTo"], $"1 {Loc["Unit_GB"]}"), Loc["Share_InfoLitterbox"]),
        ShareDestination.Catbox => (Loc["Share_InfoPermanent"],
            string.Format(Loc["Share_InfoAnonymousUpTo"], $"200 {Loc["Unit_MB"]}"), Loc["Share_InfoCatbox"]),
        ShareDestination.GoFile => (Loc["Share_InfoGoFileTitle"], Loc["Share_InfoAnonymousNoLimit"], Loc["Share_InfoGoFile"]),
        ShareDestination.PixelDrain => (Loc["Share_InfoPixelDrainTitle"], Loc["Share_InfoPixelDrainFacts"], Loc["Share_InfoPixelDrain"]),
        _ => (Loc["Share_InfoFileOnlyTitle"], Loc["Share_InfoFileOnlyFacts"], Loc["Share_InfoFileOnly"])
    };

    /// <summary>The custom quality's choices, also offered in Settings.</summary>
    public static readonly int[] CustomHeights = { 0, 2160, 1440, 1080, 720, 540, 480 };
    public static readonly int[] CustomFrameRates = { 0, 120, 60, 30 };

    /// <summary>The size or host first, large and bold; the resolution or limits beside it; a quieter sentence below.</summary>
    private sealed class InfoBlock
    {
        private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
        private readonly TextBlock _facts;
        private readonly TextBlock _detail;

        public Border Root { get; }

        public InfoBlock(Func<string, Brush> brush)
        {
            _facts = new TextBlock
            {
                FontSize = 13,
                Foreground = brush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            _detail = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = brush("TextFillColorTertiaryBrush") };
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_facts, 1);
            line.Children.Add(_title);
            line.Children.Add(_facts);
            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(line);
            stack.Children.Add(_detail);
            Root = new Border
            {
                Child = stack,
                Background = brush("CardBackgroundFillColorSecondaryBrush"),
                BorderBrush = brush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 9, 12, 10)
            };
        }

        public void Set(string title, string facts, string detail)
        {
            _title.Text = title;
            _facts.Text = facts;
            _facts.Visibility = string.IsNullOrEmpty(facts) ? Visibility.Collapsed : Visibility.Visible;
            _detail.Text = detail;
        }
    }
}
