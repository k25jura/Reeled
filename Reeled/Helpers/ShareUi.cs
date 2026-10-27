using System;
using System.ComponentModel;
using System.IO;
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
        var compressedButton = Segment(string.Format(loc["Share_QualityCompressed"], prefs.TargetSizeMb));
        var originalButton = Segment(loc["Share_QualityOriginal"]);
        var qualityRow = new Grid { ColumnSpacing = 6 };
        qualityRow.ColumnDefinitions.Add(new ColumnDefinition());
        qualityRow.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(originalButton, 1);
        qualityRow.Children.Add(compressedButton);
        qualityRow.Children.Add(originalButton);
        var qualityHint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorTertiaryBrush") };

        // ---- Destination ----
        var destinations = new[] { ShareDestination.Litterbox, ShareDestination.Catbox, ShareDestination.GoFile, ShareDestination.PixelDrain, ShareDestination.FileOnly };
        var destinationBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var d in destinations) destinationBox.Items.Add(DestinationName(d, prefs));
        destinationBox.SelectedIndex = Math.Max(0, Array.IndexOf(destinations, prefs.Destination));
        var destinationHint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextFillColorTertiaryBrush") };

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
        var noticeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
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
        options.Children.Add(qualityHint);
        options.Children.Add(Label(loc["Share_Destination"]));
        options.Children.Add(destinationBox);
        options.Children.Add(destinationHint);
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
            SegmentedButtons.Select(quality == ShareQuality.Compressed ? compressedButton : originalButton, compressedButton, originalButton);
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
            qualityHint.Text = quality == ShareQuality.Compressed
                ? (alreadySmall
                    ? string.Format(loc["Share_HintAlreadySmall"], Mb(originalBytes))
                    : string.Format(loc["Share_HintCompressed"], prefs.TargetSizeMb, PlanText(seconds, target, subject)))
                : string.Format(loc["Share_HintOriginal"], Mb(originalBytes));

            long uploadBytes = quality == ShareQuality.Compressed ? Math.Min(target, Math.Max(originalBytes, 1)) : originalBytes;
            destinationHint.Text = DestinationHint(destination, prefs);

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
            resultText.Text = done && job.OutputBytes > 0 && job.Request.Quality == ShareQuality.Compressed
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

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} MB";

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

    /// <summary>"720p, 60 fps" — what the size limit buys for a clip this long.</summary>
    private static string PlanText(double seconds, long target, GameClip clip)
    {
        var plan = ClipCompressor.PlanFor(TimeSpan.FromSeconds(seconds), target, (int)clip.VideoHeight);
        bool cap = plan.Cap30Fps && !(clip.Framerate is > 0 and <= 31);
        int fps = cap ? 30 : (int)Math.Round(clip.Framerate > 0 ? clip.Framerate : 60);
        return $"{plan.MaxHeight}p, {fps} fps";
    }

    public static string DestinationName(ShareDestination destination, SharePreferences prefs) => destination switch
    {
        ShareDestination.Litterbox => string.Format(Loc["Share_DestLitterbox"], prefs.LitterboxExpiry.Replace("h", " h")),
        ShareDestination.Catbox => Loc["Share_DestCatbox"],
        ShareDestination.GoFile => Loc["Share_DestGoFile"],
        ShareDestination.PixelDrain => Loc["Share_DestPixelDrain"],
        _ => Loc["Share_DestFileOnly"]
    };

    private static string DestinationHint(ShareDestination destination, SharePreferences prefs) => destination switch
    {
        ShareDestination.Litterbox => string.Format(Loc["Share_HintLitterbox"], prefs.LitterboxExpiry.Replace("h", " h")),
        ShareDestination.Catbox => Loc["Share_HintCatbox"],
        ShareDestination.GoFile => Loc["Share_HintGoFile"],
        ShareDestination.PixelDrain => Loc["Share_HintPixelDrain"],
        _ => Loc["Share_HintFileOnly"]
    };
}
