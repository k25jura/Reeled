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
    private bool _isIndicatorVisible = false;
    private Storyboard? _indicatorStoryboard;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        Loaded += (s, e) =>
        {
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

    private void UpdateThemeVisuals(ElementTheme theme)
    {
        UpdateLogo(theme);
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.RequestedTheme = theme;
        }
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
            "StorageSection" => StorageSection,
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

    private void SetActiveCategory(string sectionTag, bool animate)
    {
        if (_activeSectionTag == sectionTag && _isIndicatorVisible) return;
        _activeSectionTag = sectionTag;

        var buttons = new[] { CatFoldersBtn, CatPlaybackBtn, CatAppearanceBtn, CatStorageBtn, CatAboutBtn };
        var icons = new[] { CatFoldersIcon, CatPlaybackIcon, CatAppearanceIcon, CatStorageIcon, CatAboutIcon };
        var texts = new[] { CatFoldersText, CatPlaybackText, CatAppearanceText, CatStorageText, CatAboutText };

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
                if (animate)
                {
                    var sb = new Storyboard();
                    var anim = new DoubleAnimation
                    {
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(isSelected ? 180 : 150),
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

            // Update text font weight & icon color
            if (texts[i] != null)
            {
                texts[i].FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }

            if (icons[i] != null)
            {
                icons[i].Foreground = isSelected
                    ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }

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
                IndicatorTranslation.Y = targetY;
                ActiveIndicatorPill.Opacity = 1.0;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                _isIndicatorVisible = true;
                return;
            }

            if (!animate)
            {
                if (_indicatorStoryboard != null)
                {
                    _indicatorStoryboard.Stop();
                    _indicatorStoryboard = null;
                }
                _currentIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                return;
            }

            if (Math.Abs(_currentIndicatorY - targetY) < 1.0)
                return;

            if (_indicatorStoryboard != null)
            {
                _indicatorStoryboard.Stop();
                _indicatorStoryboard = null;
            }

            var sb = new Storyboard();
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            var animY = new DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = ease
            };
            Storyboard.SetTarget(animY, IndicatorTranslation);
            Storyboard.SetTargetProperty(animY, "Y");
            sb.Children.Add(animY);

            sb.Completed += (s, e) =>
            {
                _currentIndicatorY = targetY;
            };

            _indicatorStoryboard = sb;
            sb.Begin();
        }
        catch { }
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
            else if (IsSectionAtOrAbove(StorageSection, threshold))
                SetActiveCategory("StorageSection", animate: true);
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
