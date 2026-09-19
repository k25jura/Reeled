using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private bool _isInitializing;
    private bool _isProgrammaticScroll;
    private string _activeSectionTag = "FoldersSection";
    private double _currentIndicatorY = 0;
    private double _targetIndicatorY = 0;
    private bool _isIndicatorVisible = false;
    private Storyboard? _indicatorStoryboard;
    private Storyboard? _updateCardStoryboard;
    private DispatcherTimer? _foldersStatusTimer;
    private DispatcherTimer? _storageStatusTimer;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.FolderStatusMessage) && !string.IsNullOrEmpty(ViewModel.FolderStatusMessage))
            {
                bool isError = ViewModel.FolderStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(FoldersStatusBorder, FoldersStatusTranslation, FoldersStatusText, FoldersStatusIcon, ViewModel.FolderStatusMessage, isError, ref _foldersStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.StorageStatusMessage) && !string.IsNullOrEmpty(ViewModel.StorageStatusMessage))
            {
                bool isError = ViewModel.StorageStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(StorageStatusBorder, StorageStatusTranslation, StorageStatusText, StorageStatusIcon, ViewModel.StorageStatusMessage, isError, ref _storageStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.IsUpdateAvailable))
            {
                AnimateUpdateCard(ViewModel.IsUpdateAvailable);
            }
        };

        Loaded += (s, e) =>
        {
            ApplyLocalization(ViewModel.Loc);
            UpdateThemeVisuals(ActualTheme);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                SetActiveCategory("FoldersSection", animate: false);
            });
        };

        ActualThemeChanged += (s, e) => UpdateThemeVisuals(ActualTheme);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isInitializing = true;
        ViewModel.Initialize();

        ThemeComboBox.SelectedIndex = ViewModel.SelectedThemeIndex;
        LanguageComboBox.SelectedIndex = ViewModel.SelectedLanguageIndex;
        ApplyLocalization(ViewModel.Loc);
        UpdateThemeVisuals(ActualTheme);

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

        _isInitializing = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ThemeComboBox.SelectedIndex >= 0)
        {
            ViewModel.SetAppTheme(ThemeComboBox.SelectedIndex);
            UpdateThemeVisuals(ActualTheme);
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageComboBox.SelectedIndex >= 0)
        {
            ViewModel.SetLanguage(LanguageComboBox.SelectedIndex);
            ApplyLocalization(ViewModel.Loc);
            UpdateCategoryButtonColors(ActualTheme);
        }
    }

    private void UpdateThemeVisuals(ElementTheme theme)
    {
        UpdateLogo(theme);
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.RequestedTheme = theme;
        }
        UpdateCategoryButtonColors(theme);
    }

    private void UpdateLogo(ElementTheme theme)
    {
        if (SettingsLogoSvg == null) return;
        bool isLight = (theme == ElementTheme.Light);
        var uri = isLight 
            ? new System.Uri("ms-appx:///Assets/dark-banner.svg") 
            : new System.Uri("ms-appx:///Assets/light-banner.svg");
        if (SettingsLogoSvg.UriSource != uri)
        {
            SettingsLogoSvg.UriSource = uri;
        }
    }

    private void OnDefaultSpeedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddFolderAsync(App.WindowHandle);
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
        sb.Begin();
    }

    private void ScrollToSection(string sectionName)
    {
        FrameworkElement? target = sectionName switch
        {
            "FoldersSection" => FoldersSection,
            "PlaybackSection" => PlaybackSection,
            "AppearanceSection" => AppearanceSection,
            "LocalizationSection" => LocalizationSection,
            "StorageSection" => StorageSection,
            "UpdateSection" => UpdateSection,
            "AboutSection" => AboutSection,
            _ => null
        };

        if (target != null && SettingsScrollViewer != null && SettingsContentStack != null)
        {
            try
            {
                var transform = target.TransformToVisual(SettingsContentStack);
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
        var buttons = new[] { CatFoldersBtn, CatPlaybackBtn, CatAppearanceBtn, CatLanguageBtn, CatStorageBtn, CatUpdatesBtn, CatAboutBtn };
        var icons = new[] { CatFoldersIcon, CatPlaybackIcon, CatAppearanceIcon, CatLanguageIcon, CatStorageIcon, CatUpdatesIcon, CatAboutIcon };
        var texts = new[] { CatFoldersText, CatPlaybackText, CatAppearanceText, CatLanguageText, CatStorageText, CatUpdatesText, CatAboutText };

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

        var buttons = new[] { CatFoldersBtn, CatPlaybackBtn, CatAppearanceBtn, CatLanguageBtn, CatStorageBtn, CatUpdatesBtn, CatAboutBtn };
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
                    sb.Begin();
                }
                else
                {
                    selectedBorder.Opacity = targetOpacity;
                }
            }
        }

        UpdateCategoryButtonColors(ActualTheme);

        if (selectedBtn != null)
        {
            AnimateIndicatorTo(selectedBtn, animate);
        }
    }

    private void AnimateIndicatorTo(Button targetBtn, bool animate)
    {
        if (ActiveIndicatorPill == null || CategoriesNavRoot == null || IndicatorTranslation == null)
            return;

        try
        {
            var transform = targetBtn.TransformToVisual(CategoriesNavRoot);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double targetY = pt.Y + (targetBtn.ActualHeight - 18.0) / 2.0;

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

            if (!animate)
            {
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
            moveSb.Begin();
        }
        catch { }
    }

    private void ShowStatusBanner(Border border, TranslateTransform trans, TextBlock textBlock, FontIcon icon, string message, bool isError, ref DispatcherTimer? timer)
    {
        if (border == null || trans == null || textBlock == null || icon == null) return;

        timer?.Stop();
        timer = null;

        bool isLight = (ActualTheme == ElementTheme.Light);
        textBlock.Text = message;
        if (isError)
        {
            icon.Glyph = "\uE783";
            var critBrush = new SolidColorBrush(isLight ? Windows.UI.Color.FromArgb(255, 196, 43, 28) : Windows.UI.Color.FromArgb(255, 255, 153, 164));
            icon.Foreground = critBrush;
            textBlock.Foreground = critBrush;
        }
        else
        {
            icon.Glyph = "\uE73E";
            var accentBrush = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 95, 184))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
            var textBrush = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 240, 240));

            icon.Foreground = accentBrush;
            textBlock.Foreground = textBrush;
        }

        // Accordion space-reserving expansion animation with Apple QuarticEase
        var openSb = new Storyboard();
        var appleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };

        var animHeight = new DoubleAnimation
        {
            To = 38.0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = appleEase,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animHeight, border);
        Storyboard.SetTargetProperty(animHeight, "Height");
        openSb.Children.Add(animHeight);

        var animOpacity = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = appleEase
        };
        Storyboard.SetTarget(animOpacity, border);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");
        openSb.Children.Add(animOpacity);

        var animTrans = new DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = appleEase
        };
        Storyboard.SetTarget(animTrans, trans);
        Storyboard.SetTargetProperty(animTrans, "Y");
        openSb.Children.Add(animTrans);

        openSb.Begin();

        // Auto-collapse after 3.5s with graceful reverse animation
        var collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3500) };
        collapseTimer.Tick += (s, e) =>
        {
            collapseTimer.Stop();
            var closeSb = new Storyboard();
            var animCloseHeight = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(190),
                EasingFunction = appleEase,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animCloseHeight, border);
            Storyboard.SetTargetProperty(animCloseHeight, "Height");
            closeSb.Children.Add(animCloseHeight);

            var animCloseOpacity = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animCloseOpacity, border);
            Storyboard.SetTargetProperty(animCloseOpacity, "Opacity");
            closeSb.Children.Add(animCloseOpacity);

            var animCloseTrans = new DoubleAnimation
            {
                To = -8.0,
                Duration = TimeSpan.FromMilliseconds(190),
                EasingFunction = appleEase
            };
            Storyboard.SetTarget(animCloseTrans, trans);
            Storyboard.SetTargetProperty(animCloseTrans, "Y");
            closeSb.Children.Add(animCloseTrans);

            closeSb.Begin();
        };
        timer = collapseTimer;
        collapseTimer.Start();
    }

    private void OnSettingsScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isProgrammaticScroll)
        {
            if (!e.IsIntermediate)
            {
                _isProgrammaticScroll = false;
            }
            return;
        }

        UpdateActiveCategoryFromScroll();
    }

    private void UpdateActiveCategoryFromScroll()
    {
        if (SettingsScrollViewer == null) return;

        try
        {
            double scrollY = SettingsScrollViewer.VerticalOffset;
            double maxScroll = SettingsScrollViewer.ScrollableHeight;

            // When reached or close to the bottom of scrollable content (within 32px), select AboutSection
            if (maxScroll > 0 && (maxScroll - scrollY) <= 32)
            {
                SetActiveCategory("AboutSection", animate: true);
                return;
            }

            // Check sections from bottom to top
            const double threshold = 160;

            if (IsSectionAtOrAbove(AboutSection, threshold))
                SetActiveCategory("AboutSection", animate: true);
            else if (IsSectionAtOrAbove(UpdateSection, threshold))
                SetActiveCategory("UpdateSection", animate: true);
            else if (IsSectionAtOrAbove(StorageSection, threshold))
                SetActiveCategory("StorageSection", animate: true);
            else if (IsSectionAtOrAbove(LocalizationSection, threshold))
                SetActiveCategory("LocalizationSection", animate: true);
            else if (IsSectionAtOrAbove(AppearanceSection, threshold))
                SetActiveCategory("AppearanceSection", animate: true);
            else if (IsSectionAtOrAbove(PlaybackSection, threshold))
                SetActiveCategory("PlaybackSection", animate: true);
            else
                SetActiveCategory("FoldersSection", animate: true);
        }
        catch
        {
        }
    }

    private void AnimateUpdateCard(bool open)
    {
        if (UpdateAvailableBorder == null || UpdateAvailableTranslation == null) return;
        _updateCardStoryboard?.Stop();

        var sb = new Storyboard();
        var appleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };

        double targetHeight = open ? 120.0 : 0.0;
        double targetOpacity = open ? 1.0 : 0.0;
        double targetY = open ? 0.0 : -10.0;

        var animH = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = appleEase,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animH, UpdateAvailableBorder);
        Storyboard.SetTargetProperty(animH, "Height");
        sb.Children.Add(animH);

        var animO = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(open ? 200 : 160),
            EasingFunction = appleEase
        };
        Storyboard.SetTarget(animO, UpdateAvailableBorder);
        Storyboard.SetTargetProperty(animO, "Opacity");
        sb.Children.Add(animO);

        var animT = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = appleEase
        };
        Storyboard.SetTarget(animT, UpdateAvailableTranslation);
        Storyboard.SetTargetProperty(animT, "Y");
        sb.Children.Add(animT);

        sb.Completed += (s, e) =>
        {
            UpdateAvailableBorder.Height = targetHeight;
            UpdateAvailableBorder.Opacity = targetOpacity;
            UpdateAvailableTranslation.Y = targetY;
            UpdateAvailableBorder.IsHitTestVisible = open;
        };

        if (open) UpdateAvailableBorder.IsHitTestVisible = true;
        _updateCardStoryboard = sb;
        sb.Begin();
    }

    private void ApplyLocalization(Reeled.Services.ILocalizationService loc)
    {
        if (loc == null) return;

        // Categories
        if (CatFoldersText != null) CatFoldersText.Text = loc["Cat_Folders"];
        if (CatPlaybackText != null) CatPlaybackText.Text = loc["Cat_Playback"];
        if (CatAppearanceText != null) CatAppearanceText.Text = loc["Cat_Appearance"];
        if (CatLanguageText != null) CatLanguageText.Text = loc["Cat_Language"];
        if (CatStorageText != null) CatStorageText.Text = loc["Cat_Storage"];
        if (CatUpdatesText != null) CatUpdatesText.Text = loc["Cat_Updates"];
        if (CatAboutText != null) CatAboutText.Text = loc["Cat_About"];

        // Localization Section
        if (LocalizationSectionTitle != null) LocalizationSectionTitle.Text = loc["Language_SectionTitle"];
        if (AppLanguageTitleText != null) AppLanguageTitleText.Text = loc["Language_AppLanguageTitle"];
        if (AppLanguageSubtitleText != null) AppLanguageSubtitleText.Text = loc["Language_AppLanguageSubtitle"];
        if (DateFormatTitleText != null) DateFormatTitleText.Text = loc["Language_DateFormatTitle"];
        if (DateFormatSubtitleText != null) DateFormatSubtitleText.Text = loc["Language_DateFormatSubtitle"];

        // Storage Section (Moments)
        if (StorageMomentsTitleText != null) StorageMomentsTitleText.Text = loc["Storage_MomentsTitle"];
        if (StorageMomentsSubtitleText != null) StorageMomentsSubtitleText.Text = loc["Storage_MomentsSubtitle"];
        if (ClearMomentsButton != null) ClearMomentsButton.Content = loc["Storage_MomentsButton"];

        // Updates Section
        if (UpdatesSectionTitle != null) UpdatesSectionTitle.Text = loc["Updates_SectionTitle"];
        if (AutoCheckUpdatesTitleText != null) AutoCheckUpdatesTitleText.Text = loc["Updates_AutoCheckTitle"];
        if (AutoCheckUpdatesSubtitleText != null) AutoCheckUpdatesSubtitleText.Text = loc["Updates_AutoCheckSubtitle"];
        if (DownloadUpdateButton != null) DownloadUpdateButton.Content = loc["Updates_DownloadButton"];
        if (InstallUpdateButton != null) InstallUpdateButton.Content = loc["Updates_InstallButton"];
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
