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
    private bool _isInitializing = true;
    private bool _isProgrammaticScroll;
    private string _activeSectionTag = "FoldersSection";
    private double _currentIndicatorY = 0;
    private double _targetIndicatorY = 0;
    private bool _isIndicatorVisible = false;
    private Storyboard? _indicatorStoryboard;
    private Storyboard? _updateCardStoryboard;
    private Storyboard? _downloadTransitionStoryboard;
    private DispatcherTimer? _foldersStatusTimer;
    private DispatcherTimer? _storageStatusTimer;
    private DispatcherTimer? _updatesStatusTimer;
    private DispatcherTimer? _aboutStatusTimer;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.FolderStatusMessage) && !string.IsNullOrEmpty(ViewModel.FolderStatusMessage))
            {
                bool isError = ViewModel.FolderStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || ViewModel.FolderStatusMessage.StartsWith("Помилка", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(FoldersStatusBorder, FoldersStatusTranslation, FoldersStatusText, FoldersStatusIcon, ViewModel.FolderStatusMessage, isError, ref _foldersStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.StorageStatusMessage) && !string.IsNullOrEmpty(ViewModel.StorageStatusMessage))
            {
                bool isError = ViewModel.StorageStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || ViewModel.StorageStatusMessage.StartsWith("Помилка", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(StorageStatusBorder, StorageStatusTranslation, StorageStatusText, StorageStatusIcon, ViewModel.StorageStatusMessage, isError, ref _storageStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.UpdatesStatusMessage) && !string.IsNullOrEmpty(ViewModel.UpdatesStatusMessage))
            {
                bool isError = ViewModel.UpdatesStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || ViewModel.UpdatesStatusMessage.StartsWith("Помилка", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(UpdatesStatusBorder, UpdatesStatusTranslation, UpdatesStatusText, UpdatesStatusIcon, ViewModel.UpdatesStatusMessage, isError, ref _updatesStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.AboutStatusMessage) && !string.IsNullOrEmpty(ViewModel.AboutStatusMessage))
            {
                bool isError = ViewModel.AboutStatusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || ViewModel.AboutStatusMessage.StartsWith("Помилка", StringComparison.OrdinalIgnoreCase);
                ShowStatusBanner(AboutStatusBorder, AboutStatusTranslation, AboutStatusText, AboutStatusIcon, ViewModel.AboutStatusMessage, isError, ref _aboutStatusTimer);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.IsUpdateAvailable))
            {
                AnimateUpdateCard(ViewModel.IsUpdateAvailable);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.IsDownloadingUpdate))
            {
                AnimateDownloadStateTransition(ViewModel.IsDownloadingUpdate);
            }
        };

        Loaded += (s, e) =>
        {
            ApplyLocalization(ViewModel.Loc);
            UpdateThemeVisuals(ActualTheme);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _isInitializing = false;
                SetActiveCategory("FoldersSection", animate: false);
            });
        };

        ViewModel.Loc.LanguageChanged += (s, e) =>
        {
            ApplyLocalization(ViewModel.Loc);
        };

        ActualThemeChanged += (s, e) => UpdateThemeVisuals(ActualTheme);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isInitializing = true;
        ViewModel.Initialize();

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

        if (DefaultVolumeSlider != null) DefaultVolumeSlider.Value = ViewModel.Volume;
        if (DefaultRepeatComboBox != null) DefaultRepeatComboBox.SelectedIndex = ViewModel.SelectedRepeatModeIndex;
        if (AutoHideTimeoutComboBox != null) AutoHideTimeoutComboBox.SelectedIndex = ViewModel.SelectedAutoHideIndex;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isInitializing = true;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ThemeComboBox.SelectedIndex >= 0 && ThemeComboBox.SelectedIndex != ViewModel.SelectedThemeIndex)
        {
            ViewModel.SetAppTheme(ThemeComboBox.SelectedIndex);
            UpdateThemeVisuals(ActualTheme);
        }
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

    private void OnDefaultRepeatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (DefaultRepeatComboBox.SelectedIndex >= 0 && DefaultRepeatComboBox.SelectedIndex != ViewModel.SelectedRepeatModeIndex)
        {
            ViewModel.SetRepeatMode(DefaultRepeatComboBox.SelectedIndex);
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

        // Gentle space-reserving expansion animation with Apple QuarticEase (240ms)
        var openSb = new Storyboard();
        var appleEaseOut = new QuarticEase { EasingMode = EasingMode.EaseOut };
        const double durationMs = 240.0;

        var animHeight = new DoubleAnimation
        {
            From = border.ActualHeight > 0 ? border.ActualHeight : 0.0,
            To = 38.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = appleEaseOut,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animHeight, border);
        Storyboard.SetTargetProperty(animHeight, "Height");
        openSb.Children.Add(animHeight);

        var animOpacity = new DoubleAnimation
        {
            From = border.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = appleEaseOut
        };
        Storyboard.SetTarget(animOpacity, border);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");
        openSb.Children.Add(animOpacity);

        var animTrans = new DoubleAnimation
        {
            From = trans.Y,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = appleEaseOut
        };
        Storyboard.SetTarget(animTrans, trans);
        Storyboard.SetTargetProperty(animTrans, "Y");
        openSb.Children.Add(animTrans);

        openSb.Completed += (s, e) =>
        {
            border.Height = 38.0;
            border.Opacity = 1.0;
            trans.Y = 0.0;
        };

        openSb.Begin();

        // Auto-collapse after 3.5s with graceful, gentle reverse animation at the same speed (240ms)
        var collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3500) };
        collapseTimer.Tick += (s, e) =>
        {
            collapseTimer.Stop();
            var closeSb = new Storyboard();
            var gentleEase = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            var animCloseHeight = new DoubleAnimation
            {
                From = border.ActualHeight > 0 ? border.ActualHeight : 38.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = gentleEase,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animCloseHeight, border);
            Storyboard.SetTargetProperty(animCloseHeight, "Height");
            closeSb.Children.Add(animCloseHeight);

            var animCloseOpacity = new DoubleAnimation
            {
                From = border.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = gentleEase
            };
            Storyboard.SetTarget(animCloseOpacity, border);
            Storyboard.SetTargetProperty(animCloseOpacity, "Opacity");
            closeSb.Children.Add(animCloseOpacity);

            var animCloseTrans = new DoubleAnimation
            {
                From = trans.Y,
                To = -6.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = gentleEase
            };
            Storyboard.SetTarget(animCloseTrans, trans);
            Storyboard.SetTargetProperty(animCloseTrans, "Y");
            closeSb.Children.Add(animCloseTrans);

            closeSb.Completed += (s2, e2) =>
            {
                border.Height = 0.0;
                border.Opacity = 0.0;
                trans.Y = -6.0;
            };

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
        var ease = open
            ? (EasingFunctionBase)new QuarticEase { EasingMode = EasingMode.EaseOut }
            : (EasingFunctionBase)new QuarticEase { EasingMode = EasingMode.EaseInOut };

        const double durationMs = 240.0;
        double startHeight = open ? 0.0 : UpdateAvailableBorder.ActualHeight;
        double targetHeight = open ? 134.0 : 0.0;
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
        sb.Begin();
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
        sb.Begin();
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
            var uri = new Uri("https://github.com/k25jura/Reeled/issues");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch { }
    }

    private void ApplyLocalization(Reeled.Services.ILocalizationService loc)
    {
        if (loc == null) return;

        // Header
        if (SettingsTitleText != null) SettingsTitleText.Text = loc["Settings_Title"];
        if (BackToHomeButton != null) ToolTipService.SetToolTip(BackToHomeButton, loc["Settings_Back"]);

        // Categories Navigation Rail
        if (CatFoldersText != null) CatFoldersText.Text = loc["Cat_Folders"];
        if (CatPlaybackText != null) CatPlaybackText.Text = loc["Cat_Playback"];
        if (CatAppearanceText != null) CatAppearanceText.Text = loc["Cat_Appearance"];
        if (CatLanguageText != null) CatLanguageText.Text = loc["Cat_Language"];
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
        if (StartMutedTitleText != null) StartMutedTitleText.Text = loc["Playback_StartMutedTitle"];
        if (StartMutedSubtitleText != null) StartMutedSubtitleText.Text = loc["Playback_StartMutedSubtitle"];
        if (DefaultRepeatTitleText != null) DefaultRepeatTitleText.Text = loc["Playback_RepeatModeTitle"];
        if (DefaultRepeatSubtitleText != null) DefaultRepeatSubtitleText.Text = loc["Playback_RepeatModeSubtitle"];
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
        if (BackdropBlurTitleText != null) BackdropBlurTitleText.Text = loc["Appearance_BackdropBlurTitle"];
        if (BackdropBlurSubtitleText != null) BackdropBlurSubtitleText.Text = loc["Appearance_BackdropBlurSubtitle"];

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
        if (UpdateAvailableNotes != null) UpdateAvailableNotes.Text = loc["Updates_AvailableNotes"];
        if (PatchNotesText != null) PatchNotesText.Text = loc["Updates_PatchNotes"];
        if (DownloadUpdateButton != null) DownloadUpdateButton.Content = loc["Updates_DownloadButton"];
        if (InstallUpdateButton != null) InstallUpdateButton.Content = loc["Updates_InstallButton"];

        // 7. About Section
        if (AboutSectionTitle != null) AboutSectionTitle.Text = loc["Cat_About"];
        if (AboutTaglineText != null) AboutTaglineText.Text = loc["About_Tagline"];
        if (AboutCreatedByText != null) AboutCreatedByText.Text = loc["About_CreatedBy"];
        if (AboutDiagnosticsTitleText != null) AboutDiagnosticsTitleText.Text = loc["About_DiagnosticsTitle"];
        if (AboutDiagnosticsSubtitleText != null) AboutDiagnosticsSubtitleText.Text = loc["About_DiagnosticsSubtitle"];
        if (CopyDiagnosticsButtonText != null) CopyDiagnosticsButtonText.Text = loc["About_DiagnosticsCopy"];
        if (CopyDiagnosticsButton != null) ToolTipService.SetToolTip(CopyDiagnosticsButton, loc["About_DiagnosticsCopy"]);
        if (DiagOsLabel != null) DiagOsLabel.Text = loc["About_DiagOS"];
        if (DiagArchLabel != null) DiagArchLabel.Text = $"{loc["About_DiagArch"]} & {loc["About_DiagRuntime"]}";
        if (DiagMemoryLabel != null) DiagMemoryLabel.Text = loc["About_DiagMemory"];
        if (DiagLibraryLabel != null) DiagLibraryLabel.Text = loc["About_DiagLibrary"];
        if (AboutFrameworkText != null) AboutFrameworkText.Text = loc["About_Framework"];
        if (AboutVersionText != null) AboutVersionText.Text = $"{loc["About_Version"]} (.NET 8 Windows App SDK x64)";
        if (AboutGithubText != null) AboutGithubText.Text = loc["About_GitHub"];
        if (AboutReleasesText != null) AboutReleasesText.Text = loc["About_Releases"];
        if (AboutIssuesText != null) AboutIssuesText.Text = loc["About_Issues"];
        if (AboutGithubButton != null) ToolTipService.SetToolTip(AboutGithubButton, loc["About_GitHub"]);
        if (AboutReleasesButton != null) ToolTipService.SetToolTip(AboutReleasesButton, loc["About_Releases"]);
        if (AboutIssuesButton != null) ToolTipService.SetToolTip(AboutIssuesButton, loc["About_Issues"]);
        if (AboutLicenseText != null) AboutLicenseText.Text = loc["About_License"];
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
