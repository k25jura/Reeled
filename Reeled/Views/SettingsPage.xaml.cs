using Reeled.Helpers;
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Reeled.Services;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private bool _isInitializing = true;
    private bool _isProgrammaticScroll;
    private string _activeSectionTag = "FoldersSection";
    private double _currentIndicatorY = 0;
    private double _targetIndicatorY = 0;
    private bool _isIndicatorVisible = false;
    private Storyboard? _indicatorStoryboard;
    private Storyboard? _updateCardStoryboard;
    private Storyboard? _downloadTransitionStoryboard;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.FolderStatusMessage) && !string.IsNullOrEmpty(ViewModel.FolderStatusMessage))
            {
                ShowStatusToast(ViewModel.FolderStatusMessage);
                // Clear it so the same message (e.g. "up to date" twice) shows again
                ViewModel.FolderStatusMessage = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.StorageStatusMessage) && !string.IsNullOrEmpty(ViewModel.StorageStatusMessage))
            {
                ShowStatusToast(ViewModel.StorageStatusMessage);
                // Clear it so the same message (e.g. "up to date" twice) shows again
                ViewModel.StorageStatusMessage = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.UpdatesStatusMessage) && !string.IsNullOrEmpty(ViewModel.UpdatesStatusMessage))
            {
                ShowStatusToast(ViewModel.UpdatesStatusMessage);
                // Clear it so the same message (e.g. "up to date" twice) shows again
                ViewModel.UpdatesStatusMessage = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.AboutStatusMessage) && !string.IsNullOrEmpty(ViewModel.AboutStatusMessage))
            {
                ShowStatusToast(ViewModel.AboutStatusMessage);
                // Clear it so the same message (e.g. "up to date" twice) shows again
                ViewModel.AboutStatusMessage = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.IsUpdateAvailable))
            {
                if (IsLoaded) AnimateUpdateCard(ViewModel.IsUpdateAvailable);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.UpdateAvailableNotes))
            {
                RenderUpdateNotes();
            }
            else if (e.PropertyName == nameof(SettingsViewModel.IsDownloadingUpdate))
            {
                AnimateDownloadStateTransition(ViewModel.IsDownloadingUpdate);
            }
        };

        Helpers.UiZoom.Changed += (s, e) => DispatcherQueue.TryEnqueue(UpdateZoomControls);
        Helpers.Accent.Changed += (s, e) => DispatcherQueue.TryEnqueue(UpdateAccentSelection);

        Loaded += (s, e) =>
        {
            BuildSubNavigation();
            // Scrolling with the wheel is the user taking over, so it ends any programmatic-scroll lock
            // (a click on a category that is already in view never produces a final ViewChanged to clear it)
            SettingsScrollViewer.AddHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler((_, _) => _isProgrammaticScroll = false), true);
            ApplyLocalization(ViewModel.Loc);
            // An update may have been found in the background while this page wasn't shown
            RenderUpdateNotes();
            SetUpdateCardState(ViewModel.IsUpdateAvailable);
            UpdateThemeVisuals(ActualTheme);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _isInitializing = false;
                string targetCategory = ViewModel.ConsumePendingSection()
                    ?? (string.IsNullOrEmpty(_activeSectionTag) ? "FoldersSection" : _activeSectionTag);
                SetActiveCategory(targetCategory, animate: false);
                UpdateTitleBarDragArea();
            });
        };

        if (ResetDefaultsHeaderButton != null)
        {
            ResetDefaultsHeaderButton.Loaded += (s, e) => UpdateTitleBarDragArea();
            ResetDefaultsHeaderButton.SizeChanged += (s, e) => UpdateTitleBarDragArea();
        }

        this.SizeChanged += (s, e) => UpdateTitleBarDragArea();

        ViewModel.Loc.LanguageChanged += (s, e) =>
        {
            ApplyLocalization(ViewModel.Loc);
            UpdateTitleBarDragArea();
        };

        ActualThemeChanged += (s, e) =>
        {
            UpdateThemeVisuals(ActualTheme);
            UpdateTitleBarDragArea();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isInitializing = true;
        ViewModel.Initialize();

        ApplyLocalization(ViewModel.Loc);
        UpdateThemeVisuals(ActualTheme);
        UpdateTitleBarDragArea();

        SyncControlsToViewModel();
        LoadIconSources();
        LoadSharing();
    }

    private void SyncControlsToViewModel()
    {
        _isInitializing = true;
        double speed = ViewModel.DefaultPlaybackSpeed;
        foreach (ComboBoxItem item in DefaultSpeedComboBox.Items)
        {
            if (item.Tag is string tag && double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                if (Math.Abs(val - speed) < 0.01)
                {
                    DefaultSpeedComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        if (DefaultVolumeSlider != null) DefaultVolumeSlider.Value = ViewModel.Volume;
        if (MaxVolumeSlider != null) MaxVolumeSlider.Value = ViewModel.MaxVolume;
        if (DefaultRepeatComboBox != null) DefaultRepeatComboBox.SelectedIndex = ViewModel.SelectedRepeatModeIndex;
        if (SkipIntervalComboBox != null) SkipIntervalComboBox.SelectedIndex = ViewModel.SelectedSkipIntervalIndex;
        if (WheelActionComboBox != null) WheelActionComboBox.SelectedIndex = ViewModel.SelectedWheelActionIndex;
        if (AutoHideTimeoutComboBox != null) AutoHideTimeoutComboBox.SelectedIndex = ViewModel.SelectedAutoHideIndex;
        if (ThemeComboBox != null) ThemeComboBox.SelectedIndex = ViewModel.SelectedThemeIndex;
        if (LanguageComboBox != null) LanguageComboBox.SelectedIndex = ViewModel.SelectedLanguageIndex;
        _isInitializing = false;
    }

    private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        var loc = ViewModel.Loc;
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme,
            Title = loc["About_ResetDialog_Title"],
            Content = loc["About_ResetDialog_Content"],
            PrimaryButtonText = loc["About_ResetDialog_Confirm"],
            CloseButtonText = loc["Common_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowSafeAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ResetOptionsToDefaultAsync();
            SyncControlsToViewModel();
            ApplyLocalization(loc);
            UpdateThemeVisuals(ActualTheme);
            ViewModel.MarkStatus(SettingsViewModel.StatusGlyphs.Reset);
            ViewModel.AboutStatusMessage = loc["About_Reset_Success"];
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CommitIconSources();
        CommitPixelDrainKey();
        _isInitializing = true;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ThemeComboBox.SelectedIndex >= 0 && ThemeComboBox.SelectedIndex != ViewModel.SelectedThemeIndex)
        {
            ViewModel.SetAppTheme(ThemeComboBox.SelectedIndex);
            UpdateThemeVisuals(App.CurrentTheme);
        }
    }

    private void OnBlurStrengthSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || BlurStrengthComboBox.SelectedIndex < 0) return;
        ViewModel.BackdropBlurLevel = BlurStrengthComboBox.SelectedIndex;
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageComboBox.SelectedIndex >= 0 && LanguageComboBox.SelectedIndex != ViewModel.SelectedLanguageIndex)
        {
            ViewModel.SetLanguage(LanguageComboBox.SelectedIndex);
            ApplyLocalization(ViewModel.Loc);
            UpdateCategoryButtonColors(ActualTheme);
        }
    }

    public void ApplyThemeVisuals(ElementTheme theme)
    {
        this.RequestedTheme = theme;
        UpdateThemeVisuals(theme);
        UpdateTitleBarDragArea();
    }

    private void UpdateTitleBarDragArea()
    {
        if (ResetDefaultsHeaderButton == null || this.XamlRoot == null) return;
        try
        {
            var transform = ResetDefaultsHeaderButton.TransformToVisual(this);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, ResetDefaultsHeaderButton.ActualWidth, ResetDefaultsHeaderButton.ActualHeight));
            double leftOffset = Math.Max(bounds.Right + 32, 540);
            (App.Window as MainWindow)?.UpdateSettingsTitleBarMargin(leftOffset);
        }
        catch
        {
            (App.Window as MainWindow)?.UpdateSettingsTitleBarMargin(540);
        }
    }

    private void UpdateThemeVisuals(ElementTheme theme)
    {
        UpdateLogo(theme);
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.RequestedTheme = theme;
        }
        if (SettingsContentRoot != null)
        {
            SettingsContentRoot.RequestedTheme = theme;
        }
        if (CategoriesNavRoot != null)
        {
            CategoriesNavRoot.RequestedTheme = theme;
        }
        UpdateCategoryButtonColors(theme);
        // Code-built visuals that hold fixed brushes are repainted for the new theme
        if (UpdateNotesBlock != null && UpdateNotesBlock.Blocks.Count > 0) RenderUpdateNotes();
        UpdateSubHeaderBrushes();
        UpdateAppearanceThemeVisuals(theme);
    }

    private void UpdateLogo(ElementTheme theme)
    {
        bool isLight = (theme == ElementTheme.Light);
        var uri = isLight 
            ? new System.Uri("ms-appx:///Assets/dark-banner.svg") 
            : new System.Uri("ms-appx:///Assets/light-banner.svg");

        if (SettingsLogoSvg != null && SettingsLogoSvg.UriSource != uri)
        {
            SettingsLogoSvg.UriSource = uri;
        }
        if (UpdateLogoSvg != null && UpdateLogoSvg.UriSource != uri)
        {
            UpdateLogoSvg.UriSource = uri;
        }
    }

    private void OnDefaultSpeedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (DefaultSpeedComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double speed))
        {
            if (Math.Abs(ViewModel.DefaultPlaybackSpeed - speed) > 0.01)
            {
                ViewModel.SetDefaultPlaybackSpeed(speed);
            }
        }
    }

    private void OnDefaultVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        int newVol = (int)Math.Round(e.NewValue / 5.0) * 5;
        newVol = Math.Clamp(newVol, 0, 100);
        if (ViewModel.Volume != newVol)
        {
            ViewModel.SetVolume(newVol);
        }
    }

    private void OnMaxVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        int newVol = (int)Math.Round(e.NewValue / 10.0) * 10;
        newVol = Math.Clamp(newVol, 100, 300);
        if (ViewModel.MaxVolume != newVol)
        {
            ViewModel.SetMaxVolume(newVol);
        }
    }

    private void OnDefaultRepeatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (DefaultRepeatComboBox.SelectedIndex >= 0 && DefaultRepeatComboBox.SelectedIndex != ViewModel.SelectedRepeatModeIndex)
        {
            ViewModel.SetRepeatMode(DefaultRepeatComboBox.SelectedIndex);
        }
    }

    private void OnSkipIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (SkipIntervalComboBox.SelectedIndex >= 0 && SkipIntervalComboBox.SelectedIndex != ViewModel.SelectedSkipIntervalIndex)
        {
            ViewModel.SetSkipInterval(SkipIntervalComboBox.SelectedIndex);
        }
    }

    private void OnWheelActionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (WheelActionComboBox.SelectedIndex >= 0 && WheelActionComboBox.SelectedIndex != ViewModel.SelectedWheelActionIndex)
        {
            ViewModel.SetWheelAction(WheelActionComboBox.SelectedIndex);
        }
    }

    private void OnAutoHideSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (AutoHideTimeoutComboBox.SelectedIndex >= 0 && AutoHideTimeoutComboBox.SelectedIndex != ViewModel.SelectedAutoHideIndex)
        {
            ViewModel.SetAutoHideTimeout(AutoHideTimeoutComboBox.SelectedIndex);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddFolderAsync(App.WindowHandle);
    }

    private async void OnWatchFoldersReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // The ListView already moved the item in WatchFolders; persist and mirror it in the library
        await ViewModel.SaveFolderOrderAsync();
    }

    private bool _isDiagnosticsOpen;

    private void OnDiagnosticsToggleClick(object sender, RoutedEventArgs e)
    {
        _isDiagnosticsOpen = !_isDiagnosticsOpen;
        if (_isDiagnosticsOpen) ViewModel.UpdateDiagnostics();
        Helpers.Motion.Reveal(DiagnosticsDetails, _isDiagnosticsOpen, null, SettingsScrollViewer, OnRevealScrolling);
        DiagnosticsToggleText.Text = ViewModel.Loc[_isDiagnosticsOpen ? "About_DiagnosticsHide" : "About_DiagnosticsShow"];

        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(DiagnosticsChevronRotation, "Angle", _isDiagnosticsOpen ? 180 : 0, Helpers.Motion.Medium));
        Helpers.Motion.BeginExact(sb);
    }

    private void OnWatchFolderMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string folder)
        {
            ShowWatchFolderMenu(folder, btn, null);
        }
    }

    private void OnWatchFolderRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement row && row.Tag is string folder)
        {
            ShowWatchFolderMenu(folder, row, e.GetPosition(row));
            e.Handled = true;
        }
    }

    /// <summary>Same actions as the library sidebar's folder menu.</summary>
    private void ShowWatchFolderMenu(string folder, FrameworkElement anchor, Windows.Foundation.Point? at)
    {
        var loc = ViewModel.Loc;
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        menu.Opened += (s, e) => Helpers.CursorHelper.ShowArrowNow();
        if (XamlRoot != null) menu.XamlRoot = XamlRoot;

        void Add(MenuFlyoutItemBase item) => menu.Items.Add(item);
        MenuFlyoutItem Item(string key, string glyph, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            item.Click += (s, e) => action();
            return item;
        }

        int index = ViewModel.WatchFolders.IndexOf(folder);
        int last = ViewModel.WatchFolders.Count - 1;

        Add(Item("FolderMenu_Open", "\uE8E5", async () => await ViewModel.NavigateToDirectoryAsync(folder)));
        Add(Item("FolderMenu_RevealExplorer", "\uEC50", () => ViewModel.RevealFolder(folder)));
        if (last > 0)
        {
            Add(new MenuFlyoutSeparator());
            Add(Item("FolderMenu_MoveUp", "\uE74A", async () => await ViewModel.MoveFolderAsync(folder, index - 1), index > 0));
            Add(Item("FolderMenu_MoveDown", "\uE74B", async () => await ViewModel.MoveFolderAsync(folder, index + 1), index < last));
            Add(Item("FolderMenu_MoveTop", "\uEDDB", async () => await ViewModel.MoveFolderAsync(folder, 0), index > 0));
            Add(Item("FolderMenu_MoveBottom", "\uEDDC", async () => await ViewModel.MoveFolderAsync(folder, last), index < last));
        }
        Add(new MenuFlyoutSeparator());
        Add(Item("FolderMenu_Remove", "\uE74D", async () => await ViewModel.RemoveFolderAsync(folder)));

        if (at is Windows.Foundation.Point point) menu.ShowAt(anchor, point);
        else menu.ShowAt(anchor);
    }

    private async void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string folder)
        {
            await ViewModel.RemoveFolderAsync(folder);
        }
    }

    private async void OnDirectoryAnchorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string folderPath)
        {
            await ViewModel.NavigateToDirectoryAsync(folderPath);
        }
    }

    private void OnDirectoryAnchorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            ToolTipService.SetToolTip(btn, ViewModel.Loc["Folders_OpenTip"]);
        }
    }

    private void OnRemoveFolderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            ToolTipService.SetToolTip(btn, ViewModel.Loc["Folders_MoreTip"]);
        }
    }

    private void OnCategoryButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sectionName)
        {
            _isProgrammaticScroll = true;
            SetActiveCategory(sectionName, animate: true);
            ScrollToSection(sectionName);
        }
    }

    private void OnCategoryPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            AnimateButtonScale(btn, 0.98, 80);
        }
    }

    private void OnCategoryPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            AnimateButtonScale(btn, 1.0, 160);
        }
    }

    private static void AnimateButtonScale(Button btn, double targetScale, double durationMs)
    {
        var rootGrid = FindVisualChildByName<Grid>(btn, "RootGrid");
        if (rootGrid == null) return;

        if (rootGrid.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform { CenterX = btn.ActualWidth / 2.0, CenterY = btn.ActualHeight / 2.0 };
            rootGrid.RenderTransform = st;
        }
        else
        {
            st.CenterX = btn.ActualWidth / 2.0;
            st.CenterY = btn.ActualHeight / 2.0;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animX = new DoubleAnimation { To = targetScale, Duration = TimeSpan.FromMilliseconds(durationMs), EasingFunction = ease };
        var animY = new DoubleAnimation { To = targetScale, Duration = TimeSpan.FromMilliseconds(durationMs), EasingFunction = ease };
        Storyboard.SetTarget(animX, st);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, st);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animX);
        sb.Children.Add(animY);
        Helpers.Motion.Begin(sb);
    }

    private void ScrollToSection(string sectionName)
    {
        FrameworkElement? target = sectionName switch
        {
            "FoldersSection" => FoldersSection,
            "PlaybackSection" => PlaybackSection,
            "AppearanceSection" => AppearanceSection,
            "SharingSection" => SharingSection,
            "LocalizationSection" => LocalizationSection,
            "StorageSection" => StorageSection,
            "UpdateSection" => UpdateSection,
            "AboutSection" => AboutSection,
            _ => null
        };

        if (target != null && SettingsScrollViewer != null && SettingsContentRoot != null)
        {
            try
            {
                if (sectionName == "AboutSection")
                {
                    SettingsScrollViewer.ChangeView(null, SettingsScrollViewer.ScrollableHeight, null, false);
                    return;
                }
                var transform = target.TransformToVisual(SettingsContentRoot);
                var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                double targetY = Math.Max(0, pt.Y - 14);
                SettingsScrollViewer.ChangeView(null, targetY, null, false);
            }
            catch
            {
                // Visual tree transform fallback
            }
        }
    }

    private void UpdateCategoryButtonColors(ElementTheme theme)
    {
        var buttons = new[] { CatFoldersBtn, CatPlaybackBtn, CatAppearanceBtn, CatSharingBtn, CatLanguageBtn, CatStorageBtn, CatUpdatesBtn, CatAboutBtn };
        var icons = new[] { CatFoldersIcon, CatPlaybackIcon, CatAppearanceIcon, CatSharingIcon, CatLanguageIcon, CatStorageIcon, CatUpdatesIcon, CatAboutIcon };
        var texts = new[] { CatFoldersText, CatPlaybackText, CatAppearanceText, CatSharingText, CatLanguageText, CatStorageText, CatUpdatesText, CatAboutText };

        bool isLight = (theme == ElementTheme.Light);
        var selectedBrush = isLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var defaultTextBrush = isLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 75))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210));

        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null) continue;

            bool isSelected = (btn.Tag as string) == _activeSectionTag;
            if (texts[i] != null)
            {
                texts[i].Foreground = isSelected ? selectedBrush : defaultTextBrush;
                texts[i].FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
            if (icons[i] != null)
            {
                icons[i].Foreground = isSelected ? selectedBrush : defaultTextBrush;
            }
        }
    }

    private void SetActiveCategory(string sectionTag, bool animate)
    {
        if (_activeSectionTag == sectionTag && _isIndicatorVisible) return;
        _activeSectionTag = sectionTag;

        var buttons = new[] { CatFoldersBtn, CatPlaybackBtn, CatAppearanceBtn, CatSharingBtn, CatLanguageBtn, CatStorageBtn, CatUpdatesBtn, CatAboutBtn };
        Button? selectedBtn = null;

        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null) continue;

            bool isSelected = (btn.Tag as string) == sectionTag;
            if (isSelected) selectedBtn = btn;

            // Animate SelectedBorder opacity inside ControlTemplate
            var selectedBorder = FindVisualChildByName<Border>(btn, "SelectedBorder");
            if (selectedBorder != null)
            {
                double targetOpacity = isSelected ? 1.0 : 0.0;
                if (animate && Math.Abs(selectedBorder.Opacity - targetOpacity) > 0.01)
                {
                    var sb = new Storyboard();
                    var anim = new DoubleAnimation
                    {
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(isSelected ? 180 : 140),
                        EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                    };
                    Storyboard.SetTarget(anim, selectedBorder);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    sb.Children.Add(anim);
                    Helpers.Motion.Begin(sb);
                }
                else
                {
                    selectedBorder.Opacity = targetOpacity;
                }
            }
        }

        UpdateCategoryButtonColors(ActualTheme);
        UpdateSubNavigation(sectionTag, animate);

        if (selectedBtn != null)
        {
            AnimateIndicatorTo(selectedBtn, animate);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Indicator follower: the category buttons move while sub-lists open and close, so a fixed
    // from/to animation aims at a position that is already stale. Instead the pill eases toward the
    // button's live position every frame until it settles.
    // ---------------------------------------------------------------------------------------------

    private Button? _indicatorFollowTarget;
    private bool _isIndicatorFollowing;
    private long _indicatorFollowUntil;
    private long _lastFollowTick;

    private void FollowIndicatorTo(Button targetBtn)
    {
        _indicatorFollowTarget = targetBtn;
        _indicatorFollowUntil = Environment.TickCount64 + (long)Helpers.Motion.Long + 200;
        _indicatorStoryboard?.Stop();
        _indicatorStoryboard = null;
        if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
        if (_isIndicatorFollowing) return;
        _isIndicatorFollowing = true;
        _lastFollowTick = Environment.TickCount64;
        CompositionTarget.Rendering += OnIndicatorFollowFrame;
    }

    private void OnIndicatorFollowFrame(object? sender, object e)
    {
        if (_indicatorFollowTarget == null || CategoriesNavRoot == null || IndicatorTranslation == null)
        {
            StopIndicatorFollow();
            return;
        }

        long now = Environment.TickCount64;
        double dt = Math.Clamp(now - _lastFollowTick, 1, 50);
        _lastFollowTick = now;

        double target;
        try
        {
            var pt = _indicatorFollowTarget.TransformToVisual(CategoriesNavRoot).TransformPoint(new Windows.Foundation.Point(0, 0));
            target = pt.Y + (_indicatorFollowTarget.ActualHeight - 16.0) / 2.0;
        }
        catch
        {
            StopIndicatorFollow();
            return;
        }

        // Critically damped approach: frame-rate independent, decelerating like the rest of the app
        double k = 1 - Math.Exp(-dt / 70.0);
        double y = _currentIndicatorY + (target - _currentIndicatorY) * k;
        if (Math.Abs(target - y) < 0.3) y = target;
        _currentIndicatorY = y;
        _targetIndicatorY = target;
        IndicatorTranslation.Y = y;

        if (y == target && now > _indicatorFollowUntil) StopIndicatorFollow();
    }

    private void StopIndicatorFollow()
    {
        if (!_isIndicatorFollowing) return;
        _isIndicatorFollowing = false;
        CompositionTarget.Rendering -= OnIndicatorFollowFrame;
    }

    private void AnimateIndicatorTo(Button targetBtn, bool animate)
    {
        if (animate && _isIndicatorVisible && !Helpers.Motion.IsReduced)
        {
            FollowIndicatorTo(targetBtn);
            return;
        }
        StopIndicatorFollow();

        if (ActiveIndicatorPill == null || CategoriesNavRoot == null || IndicatorTranslation == null)
            return;

        try
        {
            var transform = targetBtn.TransformToVisual(CategoriesNavRoot);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double targetY = pt.Y + (targetBtn.ActualHeight - 16.0) / 2.0;

            if (!_isIndicatorVisible)
            {
                _currentIndicatorY = targetY;
                _targetIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                ActiveIndicatorPill.Opacity = 1.0;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                _isIndicatorVisible = true;
                return;
            }

            if (Math.Abs(_targetIndicatorY - targetY) < 1.0 && _isIndicatorVisible)
            {
                return;
            }

            double fromY = _currentIndicatorY;
            _targetIndicatorY = targetY;

            if (_indicatorStoryboard != null)
            {
                _indicatorStoryboard.Stop();
                _indicatorStoryboard = null;
                IndicatorTranslation.Y = fromY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
            }

            var storage = App.GetService<ILocalStorageService>();
            bool reduceMotion = Helpers.Motion.IsReduced;

            if (!animate || reduceMotion)
            {
                if (reduceMotion && animate)
                {
                    _currentIndicatorY = targetY;
                    var fadeOut = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(60) };
                    var fadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(100) };
                    var sb = new Storyboard();
                    sb.Children.Add(fadeOut);
                    Storyboard.SetTarget(fadeOut, ActiveIndicatorPill);
                    Storyboard.SetTargetProperty(fadeOut, "Opacity");
                    sb.Completed += (s, e) =>
                    {
                        IndicatorTranslation.Y = targetY;
                        if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                        var sbIn = new Storyboard();
                        sbIn.Children.Add(fadeIn);
                        Storyboard.SetTarget(fadeIn, ActiveIndicatorPill);
                        Storyboard.SetTargetProperty(fadeIn, "Opacity");
                        Helpers.Motion.Begin(sbIn);
                    };
                    Helpers.Motion.Begin(sb);
                    return;
                }

                _currentIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                ActiveIndicatorPill.Opacity = 1.0;
                return;
            }

            double distance = Math.Abs(targetY - fromY);
            var moveSb = new Storyboard();
            var appleEase = new QuarticEase
            {
                EasingMode = EasingMode.EaseOut
            };

            // 1. Vertical Glide Animation (Y translation) with Apple fluid decelerate (exact parity with HomePage sidebar)
            var animTranslate = new DoubleAnimation
            {
                From = fromY,
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = appleEase
            };
            moveSb.Children.Add(animTranslate);
            Storyboard.SetTarget(animTranslate, IndicatorTranslation);
            Storyboard.SetTargetProperty(animTranslate, "Y");

            // 2. Subtle fluid stretch
            if (distance > 5.0 && IndicatorScale != null)
            {
                double stretch = Math.Min(1.15, 1.0 + (distance / 500.0));
                var animScaleKeyFrames = new DoubleAnimationUsingKeyFrames();
                animScaleKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame
                {
                    Value = 1.0,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
                });
                animScaleKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = stretch,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                    EasingFunction = appleEase
                });
                animScaleKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = 1.0,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                    EasingFunction = appleEase
                });

                moveSb.Children.Add(animScaleKeyFrames);
                Storyboard.SetTarget(animScaleKeyFrames, IndicatorScale);
                Storyboard.SetTargetProperty(animScaleKeyFrames, "ScaleY");
            }

            moveSb.Completed += (s, e) =>
            {
                IndicatorTranslation.Y = targetY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                _currentIndicatorY = targetY;
            };

            _currentIndicatorY = targetY;
            _indicatorStoryboard = moveSb;
            Helpers.Motion.Begin(moveSb);
        }
        catch { }
    }

    /// <summary>Section banners are replaced by one toast that is always in view.</summary>
    private void ShowStatusToast(string message)
    {
        ShowToast(message, ViewModel.StatusGlyph, ViewModel.StatusIsError);
        ViewModel.MarkStatus(SettingsViewModel.StatusGlyphs.Done);
    }

    // ---------------------------------------------------------------------------------------------
    // Sub-categories: group labels inside each section, mirrored under the active category in the
    // left navigation (they open with the category and follow the scroll position)
    // ---------------------------------------------------------------------------------------------

    private static readonly (string Section, string[] Headers)[] SubCategories =
    {
        ("PlaybackSection", new[] { "SubPlaybackSpeed", "SubPlaybackBehavior", "SubPlaybackControls", "SubPlaybackBar", "SubPlaybackOsd", "SubPlaybackDecoding" }),
        ("AppearanceSection", new[] { "SubAppearanceTheme", "SubAppearanceLayout", "SubAppearanceLibrary", "SubAppearanceEffects" }),
        ("SharingSection", new[] { "SubSharingCompression", "SubSharingUpload" }),
        ("StorageSection", new[] { "SubStoragePerformance", "SubStorageCaches" }),
        ("UpdateSection", new[] { "SubUpdatesStatus", "SubUpdatesPreferences" }),
    };

    private readonly Dictionary<string, StackPanel> _subNavPanels = new();
    private readonly List<(Button Button, TextBlock Label, TextBlock Header, string Section)> _subNavItems = new();
    private string? _activeSubHeader;
    private string? _expandedSubSection;

    private void BuildSubNavigation()
    {
        if (_subNavPanels.Count > 0 || CategoriesStackPanel == null) return;
        var loc = ViewModel.Loc;

        foreach (var (section, headers) in SubCategories)
        {
            int index = -1;
            for (int i = 0; i < CategoriesStackPanel.Children.Count; i++)
            {
                if (CategoriesStackPanel.Children[i] is Button b && b.Tag as string == section) { index = i; break; }
            }
            if (index < 0) continue;

            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(28, 0, 0, 2), Visibility = Visibility.Collapsed, Opacity = 0, RenderTransform = new TranslateTransform() };
            foreach (var name in headers)
            {
                if (FindName(name) is not TextBlock header) continue;
                var label = new TextBlock
                {
                    Text = loc["Settings" + name],
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", this)
                };
                var button = new Button
                {
                    Content = label,
                    Tag = name,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 5, 10, 6),
                    CornerRadius = new CornerRadius(4)
                };
                button.Click += (s, e) => OnSubCategoryClick(section, header, name);
                panel.Children.Add(button);
                _subNavItems.Add((button, label, header, section));
            }
            CategoriesStackPanel.Children.Insert(index + 1, panel);
            _subNavPanels[section] = panel;
        }
    }

    private void OnSubCategoryClick(string section, FrameworkElement header, string name)
    {
        _isProgrammaticScroll = true;
        SetActiveCategory(section, animate: true);
        SetActiveSubHeader(name);
        try
        {
            var pt = header.TransformToVisual(SettingsContentRoot).TransformPoint(new Windows.Foundation.Point(0, 0));
            SettingsScrollViewer.ChangeView(null, Math.Max(0, pt.Y - 14), null, false);
        }
        catch { }
    }

    private Button? FindCategoryButton(string tag)
    {
        if (CategoriesStackPanel == null) return null;
        foreach (var child in CategoriesStackPanel.Children)
        {
            if (child is Button b && b.Tag as string == tag) return b;
        }
        return null;
    }

    private void SetActiveSubHeader(string? name)
    {
        if (_activeSubHeader == name) return;
        _activeSubHeader = name;
        UpdateSubHeaderBrushes();
    }

    private void UpdateSubHeaderBrushes()
    {
        string? name = _activeSubHeader;
        var primary = Helpers.ThemeBrushes.Get("TextFillColorPrimaryBrush", this);
        var tertiary = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", this);
        foreach (var (_, label, header, _) in _subNavItems)
        {
            bool active = header.Name == name;
            label.Foreground = active ? primary : tertiary;
            label.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    /// <summary>The group label nearest above the focus line in the active section.</summary>
    private void UpdateActiveSubHeaderFromScroll(string sectionTag, double focusY)
    {
        string? best = null;
        foreach (var (_, _, header, section) in _subNavItems)
        {
            if (section != sectionTag) continue;
            var pt = header.TransformToVisual(SettingsScrollViewer).TransformPoint(new Windows.Foundation.Point(0, 0));
            if (best == null || pt.Y <= focusY) best = header.Name;
        }
        SetActiveSubHeader(best);
    }

    private void LocalizeSubCategories(Reeled.Services.ILocalizationService loc)
    {
        foreach (var (_, headers) in SubCategories)
        {
            foreach (var name in headers)
            {
                if (FindName(name) is TextBlock header) header.Text = loc["Settings" + name];
            }
        }
        foreach (var (_, label, header, _) in _subNavItems)
        {
            label.Text = loc["Settings" + header.Name];
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Status toast
    // ---------------------------------------------------------------------------------------------

    private DispatcherTimer? _toastTimer;
    private bool _isToastShown;

    private AcrylicBrush? _toastAcrylic;

    /// <summary>A share that finished in the background (its dialog was hidden).</summary>
    public void ShowShareMessage(string message, bool error) => ShowToast(message, "", error);

    private void ShowToast(string message, string glyph, bool isError)
    {
        if (SettingsToast == null) return;
        SettingsToastText.Text = message;
        SettingsToastIcon.Glyph = isError ? SettingsViewModel.StatusGlyphs.Error : glyph;
        // The action's icon in the accent colour; only errors stand out in red
        SettingsToastIcon.Foreground = Helpers.ThemeBrushes.Get(isError ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush", this);
        if (_toastAcrylic == null)
        {
            _toastAcrylic = new AcrylicBrush();
            SettingsToast.Background = _toastAcrylic;
        }
        Helpers.Backdrop.ApplyAppSurface(_toastAcrylic, Helpers.ThemeBrushes.ThemeOf(this));

        _toastTimer?.Stop();
        if (!_isToastShown)
        {
            _isToastShown = true;
            SettingsToast.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            sb.Children.Add(Helpers.Motion.Spline(SettingsToast, "Opacity", 1, Helpers.Motion.Medium, from: 0));
            sb.Children.Add(Helpers.Motion.Spline(SettingsToastTranslation, "Y", 0, Helpers.Motion.Long, from: 16));
            Helpers.Motion.BeginExact(sb);
        }

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(isError ? 5000 : 3200) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer?.Stop();
            var sb = new Storyboard();
            sb.Children.Add(Helpers.Motion.Spline(SettingsToast, "Opacity", 0, Helpers.Motion.Short, accelerate: true));
            sb.Children.Add(Helpers.Motion.Spline(SettingsToastTranslation, "Y", 10, Helpers.Motion.Short, accelerate: true));
            sb.Completed += (s2, e2) =>
            {
                if (_toastTimer?.IsEnabled == true) return; // a new message arrived meanwhile
                _isToastShown = false;
                SettingsToast.Visibility = Visibility.Collapsed;
            };
            Helpers.Motion.BeginExact(sb);
        };
        _toastTimer.Start();
    }


    private bool _isRevealScrolling;

    /// <summary>Scroll that follows an expander opening: keeps scroll-spy from reacting to it.</summary>
    private void OnRevealScrolling(bool active) => _isRevealScrolling = active;

    private void OnSettingsScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isRevealScrolling) return;
        if (_isProgrammaticScroll)
        {
            if (!e.IsIntermediate)
            {
                _isProgrammaticScroll = false;
            }
            return;
        }

        QueueScrollSpy();
    }

    private bool _scrollSpyQueued;

    /// <summary>
    /// Scroll-spy runs once per dispatcher turn instead of inside ViewChanged. ViewChanged can be raised from
    /// within a layout pass, where the nav's UpdateLayout() does nothing: the sub-list then changed on the
    /// next layout with no measured difference to glide from, so it snapped. Out here layout is safe, and
    /// rapid events collapse into one update that reads the latest scroll position.
    /// </summary>
    private void QueueScrollSpy()
    {
        if (_scrollSpyQueued) return;
        _scrollSpyQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _scrollSpyQueued = false;
                if (!_isProgrammaticScroll && !_isRevealScrolling) UpdateActiveCategoryFromScroll();
            }))
        {
            _scrollSpyQueued = false;
        }
    }

    private void UpdateActiveCategoryFromScroll()
    {
        if (SettingsScrollViewer == null) return;

        try
        {
            double scrollY = SettingsScrollViewer.VerticalOffset;
            double maxScroll = SettingsScrollViewer.ScrollableHeight;

            // Find section that encompasses the focus line (Y = 120 DIPs below scroll viewer top)
            var sections = new (FrameworkElement? Section, string Tag)[]
            {
                (FoldersSection, "FoldersSection"),
                (PlaybackSection, "PlaybackSection"),
                (AppearanceSection, "AppearanceSection"),
                (SharingSection, "SharingSection"),
                (LocalizationSection, "LocalizationSection"),
                (StorageSection, "StorageSection"),
                (UpdateSection, "UpdateSection"),
                (AboutSection, "AboutSection")
            };

            const double focusY = 120.0;
            string bestTag = "FoldersSection";

            foreach (var (section, tag) in sections)
            {
                if (section == null) continue;
                var transform = section.TransformToVisual(SettingsScrollViewer);
                var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                if (pt.Y <= focusY)
                {
                    bestTag = tag;
                }
            }

            // When scrolled near the very bottom, check if AboutSection occupies at least half of the viewport
            if (maxScroll > 0 && (maxScroll - scrollY) <= 16 && AboutSection != null)
            {
                var aboutTransform = AboutSection.TransformToVisual(SettingsScrollViewer);
                var aboutPt = aboutTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
                if (aboutPt.Y <= SettingsScrollViewer.ViewportHeight / 2.0)
                {
                    bestTag = "AboutSection";
                }
            }

            SetActiveCategory(bestTag, animate: true);
            UpdateActiveSubHeaderFromScroll(bestTag, focusY);
        }
        catch
        {
        }
    }

    private void RenderUpdateNotes()
    {
        if (UpdateNotesBlock == null) return;
        Helpers.ReleaseNotes.Render(UpdateNotesBlock, ViewModel.UpdateAvailableNotes);
        if (UpdateNotesScroller != null)
        {
            UpdateNotesScroller.Visibility = UpdateNotesBlock.Blocks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SetUpdateCardState(bool open)
    {
        if (UpdateAvailableBorder == null || UpdateAvailableTranslation == null) return;
        _updateCardStoryboard?.Stop();
        UpdateAvailableBorder.Height = open ? double.NaN : 0;
        UpdateAvailableBorder.Opacity = open ? 1.0 : 0.0;
        UpdateAvailableTranslation.Y = open ? 0.0 : -8.0;
        UpdateAvailableBorder.IsHitTestVisible = open;
        if (UpdateActionButtonsContainer != null && UpdateProgressContainer != null)
        {
            bool busy = ViewModel.IsDownloadingUpdate;
            UpdateActionButtonsContainer.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            UpdateActionButtonsContainer.Opacity = busy ? 0.0 : 1.0;
            UpdateProgressContainer.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressContainer.Opacity = busy ? 1.0 : 0.0;
            if (ProgressTranslation != null) ProgressTranslation.Y = 0;
            if (ActionButtonsTranslation != null) ActionButtonsTranslation.Y = 0;
        }
    }

    private void AnimateUpdateCard(bool open)
    {
        if (UpdateAvailableBorder == null || UpdateAvailableTranslation == null) return;
        _updateCardStoryboard?.Stop();

        var sb = new Storyboard();
        var ease = open
            ? (EasingFunctionBase)new QuarticEase { EasingMode = EasingMode.EaseOut }
            : (EasingFunctionBase)new QuarticEase { EasingMode = EasingMode.EaseInOut };

        const double durationMs = 240.0;
        double startHeight = open ? 0.0 : UpdateAvailableBorder.ActualHeight;
        // Measure the real content height (release notes vary in length) instead of guessing
        double targetHeight = 0.0;
        if (open)
        {
            double width = UpdateAvailableBorder.ActualWidth > 0 ? UpdateAvailableBorder.ActualWidth : (UpdateSection?.ActualWidth ?? 600);
            UpdateAvailableBorder.Height = double.NaN;
            UpdateAvailableBorder.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
            targetHeight = UpdateAvailableBorder.DesiredSize.Height;
            UpdateAvailableBorder.Height = startHeight;
        }
        double targetOpacity = open ? 1.0 : 0.0;
        double targetY = open ? 0.0 : -8.0;

        var animH = new DoubleAnimation
        {
            From = startHeight,
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animH, UpdateAvailableBorder);
        Storyboard.SetTargetProperty(animH, "Height");
        sb.Children.Add(animH);

        var animO = new DoubleAnimation
        {
            From = UpdateAvailableBorder.Opacity,
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animO, UpdateAvailableBorder);
        Storyboard.SetTargetProperty(animO, "Opacity");
        sb.Children.Add(animO);

        var animT = new DoubleAnimation
        {
            From = UpdateAvailableTranslation.Y,
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animT, UpdateAvailableTranslation);
        Storyboard.SetTargetProperty(animT, "Y");
        sb.Children.Add(animT);

        sb.Completed += (s, e) =>
        {
            if (open)
            {
                UpdateAvailableBorder.Height = double.NaN;
            }
            else
            {
                UpdateAvailableBorder.Height = 0;
                if (UpdateActionButtonsContainer != null && ActionButtonsTranslation != null)
                {
                    UpdateActionButtonsContainer.Opacity = 1.0;
                    ActionButtonsTranslation.Y = 0.0;
                    UpdateActionButtonsContainer.Visibility = Visibility.Visible;
                }
                if (UpdateProgressContainer != null && ProgressTranslation != null)
                {
                    UpdateProgressContainer.Opacity = 0.0;
                    ProgressTranslation.Y = 6.0;
                    UpdateProgressContainer.Visibility = Visibility.Collapsed;
                }
            }
            UpdateAvailableBorder.Opacity = targetOpacity;
            UpdateAvailableTranslation.Y = targetY;
            UpdateAvailableBorder.IsHitTestVisible = open;
        };

        if (open) UpdateAvailableBorder.IsHitTestVisible = true;
        _updateCardStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    private void AnimateDownloadStateTransition(bool isDownloading)
    {
        if (UpdateActionButtonsContainer == null || UpdateProgressContainer == null ||
            ActionButtonsTranslation == null || ProgressTranslation == null) return;

        _downloadTransitionStoryboard?.Stop();
        var sb = new Storyboard();
        var appleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
        const double durationMs = 220.0;

        if (isDownloading)
        {
            UpdateProgressContainer.Visibility = Visibility.Visible;

            var animBtnOpacity = new DoubleAnimation
            {
                From = UpdateActionButtonsContainer.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animBtnOpacity, UpdateActionButtonsContainer);
            Storyboard.SetTargetProperty(animBtnOpacity, "Opacity");
            sb.Children.Add(animBtnOpacity);

            var animBtnTrans = new DoubleAnimation
            {
                From = ActionButtonsTranslation.Y,
                To = -4.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animBtnTrans, ActionButtonsTranslation);
            Storyboard.SetTargetProperty(animBtnTrans, "Y");
            sb.Children.Add(animBtnTrans);

            var animProgOpacity = new DoubleAnimation
            {
                From = UpdateProgressContainer.Opacity,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animProgOpacity, UpdateProgressContainer);
            Storyboard.SetTargetProperty(animProgOpacity, "Opacity");
            sb.Children.Add(animProgOpacity);

            var animProgTrans = new DoubleAnimation
            {
                From = 6.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animProgTrans, ProgressTranslation);
            Storyboard.SetTargetProperty(animProgTrans, "Y");
            sb.Children.Add(animProgTrans);

            sb.Completed += (s, e) =>
            {
                UpdateActionButtonsContainer.Visibility = Visibility.Collapsed;
                UpdateProgressContainer.Opacity = 1.0;
                ProgressTranslation.Y = 0.0;
            };
        }
        else
        {
            UpdateActionButtonsContainer.Visibility = Visibility.Visible;

            var animProgOpacity = new DoubleAnimation
            {
                From = UpdateProgressContainer.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animProgOpacity, UpdateProgressContainer);
            Storyboard.SetTargetProperty(animProgOpacity, "Opacity");
            sb.Children.Add(animProgOpacity);

            var animProgTrans = new DoubleAnimation
            {
                From = ProgressTranslation.Y,
                To = -4.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animProgTrans, ProgressTranslation);
            Storyboard.SetTargetProperty(animProgTrans, "Y");
            sb.Children.Add(animProgTrans);

            var animBtnOpacity = new DoubleAnimation
            {
                From = UpdateActionButtonsContainer.Opacity,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animBtnOpacity, UpdateActionButtonsContainer);
            Storyboard.SetTargetProperty(animBtnOpacity, "Opacity");
            sb.Children.Add(animBtnOpacity);

            var animBtnTrans = new DoubleAnimation
            {
                From = 6.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animBtnTrans, ActionButtonsTranslation);
            Storyboard.SetTargetProperty(animBtnTrans, "Y");
            sb.Children.Add(animBtnTrans);

            sb.Completed += (s, e) =>
            {
                UpdateProgressContainer.Visibility = Visibility.Collapsed;
                UpdateActionButtonsContainer.Opacity = 1.0;
                ActionButtonsTranslation.Y = 0.0;
            };
        }

        _downloadTransitionStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    private async void OnPatchNotesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri("https://github.com/k25jura/Reeled/releases");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch { }
    }

    private async void OnAboutGithubClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri("https://github.com/k25jura/Reeled");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch { }
    }

    private async void OnAboutIssuesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Helpers.FeedbackDialog.ShowAsync(XamlRoot);
        }
        catch { }
    }

    private void ApplyLocalization(Reeled.Services.ILocalizationService loc)
    {
        LocalizeSubCategories(loc);
        if (loc == null) return;

        // Header
        if (SettingsTitleText != null) SettingsTitleText.Text = loc["Settings_Title"];
        if (BackToHomeButton != null) ToolTipService.SetToolTip(BackToHomeButton, loc["Settings_Back"]);

        // Categories Navigation Rail
        if (CatFoldersText != null) CatFoldersText.Text = loc["Cat_Folders"];
        if (CatPlaybackText != null) CatPlaybackText.Text = loc["Cat_Playback"];
        if (CatAppearanceText != null) CatAppearanceText.Text = loc["Cat_Appearance"];
        if (CatLanguageText != null) CatLanguageText.Text = loc["Cat_Language"];
        if (CatSharingText != null) CatSharingText.Text = loc["Cat_Sharing"];
        LocalizeSharing(loc);
        if (CatStorageText != null) CatStorageText.Text = loc["Cat_Storage"];
        if (CatUpdatesText != null) CatUpdatesText.Text = loc["Cat_Updates"];
        if (CatAboutText != null) CatAboutText.Text = loc["Cat_About"];

        // 1. Watch Folders Section
        if (FoldersSectionTitle != null) FoldersSectionTitle.Text = loc["Folders_SectionTitle"];
        if (AddMediaFoldersTitle != null) AddMediaFoldersTitle.Text = loc["Folders_AddCardTitle"];
        if (AddMediaFoldersSubtitle != null) AddMediaFoldersSubtitle.Text = loc["Folders_AddCardSubtitle"];
        if (AddFolderButtonText != null) AddFolderButtonText.Text = loc["Folders_AddButton"];
        if (ConfiguredDirectoriesHeader != null) ConfiguredDirectoriesHeader.Text = loc["Folders_ConfiguredTitle"];
        if (FoldersEmptyTitleText != null) FoldersEmptyTitleText.Text = loc["Folders_EmptyTitle"];
        if (FoldersEmptySubtitleText != null) FoldersEmptySubtitleText.Text = loc["Folders_EmptySubtitle"];

        // 2. Playback Options Section
        if (PlaybackSectionTitle != null) PlaybackSectionTitle.Text = loc["Playback_SectionTitle"];
        if (DefaultSpeedTitleText != null) DefaultSpeedTitleText.Text = loc["Playback_DefaultSpeedTitle"];
        if (DefaultSpeedSubtitleText != null) DefaultSpeedSubtitleText.Text = loc["Playback_DefaultSpeedSubtitle"];
        if (RememberSpeedTitleText != null) RememberSpeedTitleText.Text = loc["Playback_RememberSpeedTitle"];
        if (RememberSpeedSubtitleText != null) RememberSpeedSubtitleText.Text = loc["Playback_RememberSpeedSubtitle"];
        if (DefaultVolumeTitleText != null) DefaultVolumeTitleText.Text = loc["Playback_VolumeTitle"];
        if (DefaultVolumeSubtitleText != null) DefaultVolumeSubtitleText.Text = loc["Playback_VolumeSubtitle"];
        if (MaxVolumeTitleText != null) MaxVolumeTitleText.Text = loc["Playback_MaxVolumeTitle"];
        if (MaxVolumeSubtitleText != null) MaxVolumeSubtitleText.Text = loc["Playback_MaxVolumeSubtitle"];
        if (StartMutedTitleText != null) StartMutedTitleText.Text = loc["Playback_StartMutedTitle"];
        if (StartMutedSubtitleText != null) StartMutedSubtitleText.Text = loc["Playback_StartMutedSubtitle"];
        if (ResumePlaybackTitleText != null) ResumePlaybackTitleText.Text = loc["Playback_ResumeTitle"];
        if (ResumePlaybackSubtitleText != null) ResumePlaybackSubtitleText.Text = loc["Playback_ResumeSubtitle"];
        if (DefaultRepeatTitleText != null) DefaultRepeatTitleText.Text = loc["Playback_RepeatModeTitle"];
        if (DefaultRepeatSubtitleText != null) DefaultRepeatSubtitleText.Text = loc["Playback_RepeatModeSubtitle"];
        if (SkipIntervalTitleText != null) SkipIntervalTitleText.Text = loc["Playback_SkipIntervalTitle"];
        if (SkipIntervalSubtitleText != null) SkipIntervalSubtitleText.Text = loc["Playback_SkipIntervalSubtitle"];
        if (WheelActionTitleText != null) WheelActionTitleText.Text = loc["Playback_WheelTitle"];
        if (WheelActionSubtitleText != null) WheelActionSubtitleText.Text = loc["Playback_WheelSubtitle"];
        if (HardwareDecodingTitleText != null) HardwareDecodingTitleText.Text = loc["Playback_HwDecodingTitle"];
        if (HardwareDecodingSubtitleText != null) HardwareDecodingSubtitleText.Text = loc["Playback_HwDecodingSubtitle"];
        if (AutoHideTitleText != null) AutoHideTitleText.Text = loc["Playback_AutoHideTitle"];
        if (AutoHideSubtitleText != null) AutoHideSubtitleText.Text = loc["Playback_AutoHideSubtitle"];
        if (AutoHideCursorTitleText != null) AutoHideCursorTitleText.Text = loc["Playback_AutoHideCursorTitle"];
        if (AutoHideCursorSubtitleText != null) AutoHideCursorSubtitleText.Text = loc["Playback_AutoHideCursorSubtitle"];
        if (OsdTitleText != null) OsdTitleText.Text = loc["Playback_OsdTitle"];
        if (OsdSubtitleText != null) OsdSubtitleText.Text = loc["Playback_OsdSubtitle"];

        // 3. Appearance Section
        if (AppearanceSectionTitle != null) AppearanceSectionTitle.Text = loc["Appearance_SectionTitle"];
        if (AppThemeTitleText != null) AppThemeTitleText.Text = loc["Appearance_ThemeTitle"];
        if (AppThemeSubtitleText != null) AppThemeSubtitleText.Text = loc["Appearance_ThemeSubtitle"];
        if (MomentsBadgesTitleText != null) MomentsBadgesTitleText.Text = loc["Appearance_MomentsBadgesTitle"];
        if (MomentsBadgesSubtitleText != null) MomentsBadgesSubtitleText.Text = loc["Appearance_MomentsBadgesSubtitle"];
        if (GameIconsTitleText != null) GameIconsTitleText.Text = loc["Appearance_GameIconsTitle"];
        if (GameIconsSubtitleText != null) GameIconsSubtitleText.Text = loc["Appearance_GameIconsSubtitle"];
        LocalizeIconSources(loc);
        if (AlwaysShowTabsTitleText != null) AlwaysShowTabsTitleText.Text = loc["Appearance_TabsAlwaysTitle"];
        if (AlwaysShowTabsSubtitleText != null) AlwaysShowTabsSubtitleText.Text = loc["Appearance_TabsAlwaysSubtitle"];
        if (ReopenTabsTitleText != null) ReopenTabsTitleText.Text = loc["Appearance_TabsReopenTitle"];
        if (ReopenTabsSubtitleText != null) ReopenTabsSubtitleText.Text = loc["Appearance_TabsReopenSubtitle"];
        if (BackdropBlurTitleText != null) BackdropBlurTitleText.Text = loc["Appearance_BackdropBlurTitle"];
        if (BackdropBlurSubtitleText != null) BackdropBlurSubtitleText.Text = loc["Appearance_BackdropBlurSubtitle"];
        if (ReduceMotionTitleText != null) ReduceMotionTitleText.Text = loc["Appearance_ReduceMotionTitle"];
        if (ReduceMotionSubtitleText != null) ReduceMotionSubtitleText.Text = loc["Appearance_ReduceMotionSubtitle"];
        if (AccentTitleText != null) AccentTitleText.Text = loc["Appearance_AccentTitle"];
        if (AccentSubtitleText != null) AccentSubtitleText.Text = loc["Appearance_AccentSubtitle"];
        if (ZoomTitleText != null) ZoomTitleText.Text = loc["Appearance_ZoomTitle"];
        if (ZoomSubtitleText != null) ZoomSubtitleText.Text = loc["Appearance_ZoomSubtitle"];
        if (ZoomResetButton != null) ZoomResetButton.Content = loc["Zoom_Reset"];
        if (CloseToLibraryTitleText != null) CloseToLibraryTitleText.Text = loc["Playback_CloseToLibraryTitle"];
        if (CloseToLibrarySubtitleText != null) CloseToLibrarySubtitleText.Text = loc["Playback_CloseToLibrarySubtitle"];
        BuildAccentSwatches();
        UpdateZoomControls();
        if (NavPositionTitleText != null) NavPositionTitleText.Text = loc["Appearance_NavPositionTitle"];
        if (NavPositionSubtitleText != null) NavPositionSubtitleText.Text = loc["Appearance_NavPositionSubtitle"];
        if (NavSectionsTitleText != null) NavSectionsTitleText.Text = loc["Appearance_NavSectionsTitle"];
        if (NavSectionsSubtitleText != null) NavSectionsSubtitleText.Text = loc["Appearance_NavSectionsSubtitle"];
        if (NavFavoritesLabel != null) NavFavoritesLabel.Text = loc["Nav_Favorites"];
        if (NavMomentsLabel != null) NavMomentsLabel.Text = loc["Nav_SavedMoments"];
        BuildNavPositionTiles();
        FillNavSectionCombos();
        LocalizePlayerSettings(loc);

        // 4. Language & Region Section
        if (LocalizationSectionTitle != null) LocalizationSectionTitle.Text = loc["Language_SectionTitle"];
        if (AppLanguageTitleText != null) AppLanguageTitleText.Text = loc["Language_AppLanguageTitle"];
        if (AppLanguageSubtitleText != null) AppLanguageSubtitleText.Text = loc["Language_AppLanguageSubtitle"];
        if (DateFormatTitleText != null) DateFormatTitleText.Text = loc["Language_DateFormatTitle"];
        if (DateFormatSubtitleText != null) DateFormatSubtitleText.Text = loc["Language_DateFormatSubtitle"];

        // Dynamic Dropdown Items (Theme, Language, Repeat Mode & Auto-Hide)
        bool prevInit = _isInitializing;
        _isInitializing = true;
        try
        {
            if (ThemeComboBox != null)
            {
                int themeIdx = ViewModel.SelectedThemeIndex;
                ThemeComboBox.ItemsSource = new string[]
                {
                    loc["Appearance_ThemeWindows"],
                    loc["Appearance_ThemeDark"],
                    loc["Appearance_ThemeLight"]
                };
                ThemeComboBox.SelectedIndex = themeIdx;
            }

            if (BlurStrengthComboBox != null)
            {
                BlurStrengthComboBox.ItemsSource = new string[]
                {
                    loc["Appearance_BlurLight"],
                    loc["Appearance_BlurBalanced"],
                    loc["Appearance_BlurStrong"]
                };
                BlurStrengthComboBox.SelectedIndex = Math.Clamp(ViewModel.BackdropBlurLevel, 0, 2);
            }
            if (BlurStrengthTitleText != null) BlurStrengthTitleText.Text = loc["Appearance_BlurStrengthTitle"];
            if (BlurStrengthSubtitleText != null) BlurStrengthSubtitleText.Text = loc["Appearance_BlurStrengthSubtitle"];

            if (LanguageComboBox != null)
            {
                int langIdx = ViewModel.SelectedLanguageIndex;
                LanguageComboBox.ItemsSource = new string[]
                {
                    loc["Language_SystemDefault"],
                    loc["Language_English"],
                    loc["Language_Ukrainian"]
                };
                LanguageComboBox.SelectedIndex = langIdx;
            }

            if (DefaultRepeatComboBox != null)
            {
                int repIdx = ViewModel.SelectedRepeatModeIndex;
                DefaultRepeatComboBox.ItemsSource = new string[]
                {
                    loc["Playback_RepeatOff"],
                    loc["Playback_RepeatOne"],
                    loc["Playback_RepeatAll"]
                };
                DefaultRepeatComboBox.SelectedIndex = repIdx;
            }

            if (SkipIntervalComboBox != null)
            {
                int skipIdx = ViewModel.SelectedSkipIntervalIndex;
                var unit = loc["Playback_SecondsFormat"];
                SkipIntervalComboBox.ItemsSource = Array.ConvertAll(ViewModels.SettingsViewModel.SkipIntervals, v => string.Format(unit, v));
                SkipIntervalComboBox.SelectedIndex = skipIdx;
            }

            if (WheelActionComboBox != null)
            {
                int wheelIdx = ViewModel.SelectedWheelActionIndex;
                WheelActionComboBox.ItemsSource = new string[]
                {
                    loc["Playback_WheelVolume"],
                    loc["Playback_WheelSeek"],
                    loc["Playback_WheelNone"]
                };
                WheelActionComboBox.SelectedIndex = wheelIdx;
            }

            if (AutoHideTimeoutComboBox != null)
            {
                int autoHideIdx = ViewModel.SelectedAutoHideIndex;
                AutoHideTimeoutComboBox.ItemsSource = new string[]
                {
                    loc["Playback_AutoHide_2s"],
                    loc["Playback_AutoHide_3s"],
                    loc["Playback_AutoHide_5s"],
                    loc["Playback_AutoHide_Never"]
                };
                AutoHideTimeoutComboBox.SelectedIndex = autoHideIdx;
            }
        }
        finally
        {
            _isInitializing = prevInit;
        }

        // 5. Performance & Storage Section
        if (StorageSectionTitle != null) StorageSectionTitle.Text = loc["Storage_SectionTitle"];
        if (SkeletonLoadingTitleText != null) SkeletonLoadingTitleText.Text = loc["Appearance_SkeletonTitle"];
        if (SkeletonLoadingSubtitleText != null) SkeletonLoadingSubtitleText.Text = loc["Appearance_SkeletonSubtitle"];
        if (EnableClipCacheTitleText != null) EnableClipCacheTitleText.Text = loc["Storage_EnableClipCacheTitle"];
        if (EnableClipCacheSubtitleText != null) EnableClipCacheSubtitleText.Text = loc["Storage_EnableClipCacheSubtitle"];
        if (ThumbnailCacheTitleText != null) ThumbnailCacheTitleText.Text = loc["Storage_ThumbnailsTitle"];
        if (ThumbnailCacheDiskLabel != null) ThumbnailCacheDiskLabel.Text = loc["Storage_DiskUsageLabel"];
        if (ClearThumbnailsButton != null) ClearThumbnailsButton.Content = loc["Storage_ThumbnailsButton"];
        if (ClipCacheTitleText != null) ClipCacheTitleText.Text = loc["Storage_ClipCacheTitle"];
        if (ClipCacheSubtitleText != null) ClipCacheSubtitleText.Text = loc["Storage_ClipCacheSubtitle"];
        if (ClipCacheStatusLabel != null) ClipCacheStatusLabel.Text = loc["Storage_StatusLabel"];
        if (ClearClipCacheButton != null) ClearClipCacheButton.Content = loc["Storage_ClipCacheButton"];
        if (StorageMomentsTitleText != null) StorageMomentsTitleText.Text = loc["Storage_MomentsTitle"];
        if (StorageMomentsSubtitleText != null) StorageMomentsSubtitleText.Text = loc["Storage_MomentsSubtitle"];
        if (StorageMomentsStatusLabel != null) StorageMomentsStatusLabel.Text = loc["Storage_StatusLabel"];
        if (ClearMomentsButton != null) ClearMomentsButton.Content = loc["Storage_MomentsButton"];

        // 6. Updates Section
        if (UpdatesSectionTitle != null) UpdatesSectionTitle.Text = loc["Updates_SectionTitle"];
        if (CheckUpdatesButtonText != null) CheckUpdatesButtonText.Text = loc["Updates_CheckButton"];
        if (AutoCheckUpdatesTitleText != null) AutoCheckUpdatesTitleText.Text = loc["Updates_AutoCheckTitle"];
        if (AutoCheckUpdatesSubtitleText != null) AutoCheckUpdatesSubtitleText.Text = loc["Updates_AutoCheckSubtitle"];
        if (AutoDownloadUpdatesTitleText != null) AutoDownloadUpdatesTitleText.Text = loc["Updates_AutoDownloadTitle"];
        if (AutoDownloadUpdatesSubtitleText != null) AutoDownloadUpdatesSubtitleText.Text = loc["Updates_AutoDownloadSubtitle"];
        if (PatchNotesText != null) PatchNotesText.Text = loc["Updates_PatchNotes"];
        if (DownloadUpdateButton != null) DownloadUpdateButton.Content = loc["Updates_DownloadButton"];
        if (InstallUpdateButton != null) InstallUpdateButton.Content = loc[ViewModel.CanSelfUpdate ? "Updates_InstallButton" : "Updates_RunInstallerButton"];

        // 7. About Section
        if (AboutSectionTitle != null) AboutSectionTitle.Text = loc["Cat_About"];
        if (AboutTaglineText != null) AboutTaglineText.Text = loc["About_Tagline"];
        if (AboutCreatedByText != null) AboutCreatedByText.Text = loc["About_CreatedBy"];
        if (AboutDiagnosticsTitleText != null) AboutDiagnosticsTitleText.Text = loc["About_DiagnosticsTitle"];
        if (AboutDiagnosticsSubtitleText != null) AboutDiagnosticsSubtitleText.Text = loc["About_DiagnosticsSubtitle"];
        if (CopyDiagnosticsButtonText != null) CopyDiagnosticsButtonText.Text = loc["About_DiagnosticsCopy"];
        if (CopyDiagnosticsButton != null) ToolTipService.SetToolTip(CopyDiagnosticsButton, loc["About_DiagnosticsCopy"]);
        if (DiagOsLabel != null) DiagOsLabel.Text = loc["About_DiagOS"];
        if (DiagnosticsToggleText != null) DiagnosticsToggleText.Text = loc[_isDiagnosticsOpen ? "About_DiagnosticsHide" : "About_DiagnosticsShow"];
        if (DiagDisplayLabel != null) DiagDisplayLabel.Text = loc["About_DiagDisplay"];
        if (DiagEngineLabel != null) DiagEngineLabel.Text = loc["About_DiagEngine"];
        if (DiagArchLabel != null) DiagArchLabel.Text = $"{loc["About_DiagArch"]} & {loc["About_DiagRuntime"]}";
        if (DiagMemoryLabel != null) DiagMemoryLabel.Text = loc["About_DiagMemory"];
        if (DiagLibraryLabel != null) DiagLibraryLabel.Text = loc["About_DiagLibrary"];
        if (AboutFrameworkText != null) AboutFrameworkText.Text = loc["About_Framework"];
        if (AboutVersionText != null) AboutVersionText.Text = $"{string.Format(loc["About_Version"], App.GetService<IUpdateService>().CurrentVersion)} (.NET 8 Windows App SDK x64)";
        if (AboutGithubText != null) AboutGithubText.Text = loc["About_GitHub"];
        if (AboutReleasesText != null) AboutReleasesText.Text = loc["About_Releases"];
        if (AboutIssuesText != null) AboutIssuesText.Text = loc["About_Issues"];
        if (AboutGithubButton != null) ToolTipService.SetToolTip(AboutGithubButton, loc["About_GitHub"]);
        if (AboutReleasesButton != null) ToolTipService.SetToolTip(AboutReleasesButton, loc["About_Releases"]);
        if (AboutIssuesButton != null) ToolTipService.SetToolTip(AboutIssuesButton, loc["About_Issues"]);
        if (AboutLicenseText != null) AboutLicenseText.Text = loc["About_License"];
        if (ResetDefaultsHeaderText != null) ResetDefaultsHeaderText.Text = loc["About_ResetDefaults_Button"];
        if (ResetDefaultsCardTitle != null) ResetDefaultsCardTitle.Text = loc["About_ResetDefaults_Title"];
        if (ResetDefaultsCardSubtitle != null) ResetDefaultsCardSubtitle.Text = loc["About_ResetDefaults_Subtitle"];
        if (ResetDefaultsCardButton != null) ResetDefaultsCardButton.Content = loc["About_ResetDefaults_Button"];

        // After every list above (and the ones filled by the section partials) has its new strings
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FitSettingsComboBoxes);
    }

    // One width for every dropdown on the page, wider only where an item needs it
    private const double SettingsComboMinWidth = 200;

    private void FitSettingsComboBoxes()
    {
        if (SettingsScrollViewer != null) FitComboBoxes(SettingsScrollViewer);
    }

    private static void FitComboBoxes(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ComboBox box)
            {
                Helpers.ComboBoxFit.Apply(box, SettingsComboMinWidth);
                continue;
            }
            FitComboBoxes(child);
        }
    }

    private bool IsSectionAtOrAbove(FrameworkElement? section, double threshold)
    {
        if (section == null || SettingsScrollViewer == null) return false;
        var transform = section.TransformToVisual(SettingsScrollViewer);
        var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        return pt.Y <= threshold;
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name) return typed;
            var desc = FindVisualChildByName<T>(child, name);
            if (desc != null) return desc;
        }
        return null;
    }
}
