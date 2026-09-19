using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private bool _isInitializing;
    private bool _isProgrammaticScroll;
    private Button? _activeCategoryButton;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        Loaded += (s, e) => UpdateThemeVisuals(ActualTheme);
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

        SetActiveCategoryButton(CatFoldersBtn);
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
            SetActiveCategoryButton(btn);
            ScrollToSection(sectionName);
        }
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

        if (target != null && SettingsScrollViewer != null)
        {
            try
            {
                var transform = target.TransformToVisual(SettingsScrollViewer);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                double targetOffset = SettingsScrollViewer.VerticalOffset + point.Y - 16;
                if (targetOffset < 0) targetOffset = 0;
                SettingsScrollViewer.ChangeView(null, targetOffset, null, false);
            }
            catch
            {
                // Visual tree transform fallback
            }
        }
    }

    private void SetActiveCategoryButton(Button? btn)
    {
        if (_activeCategoryButton == btn) return;

        var normalBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var activeBrush = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];

        if (CatFoldersBtn != null) CatFoldersBtn.Background = normalBrush;
        if (CatPlaybackBtn != null) CatPlaybackBtn.Background = normalBrush;
        if (CatAppearanceBtn != null) CatAppearanceBtn.Background = normalBrush;
        if (CatStorageBtn != null) CatStorageBtn.Background = normalBrush;
        if (CatAboutBtn != null) CatAboutBtn.Background = normalBrush;

        if (btn != null)
        {
            btn.Background = activeBrush;
            _activeCategoryButton = btn;
        }
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
        try
        {
            const double threshold = 140;

            if (IsSectionVisible(AboutSection, threshold))
                SetActiveCategoryButton(CatAboutBtn);
            else if (IsSectionVisible(StorageSection, threshold))
                SetActiveCategoryButton(CatStorageBtn);
            else if (IsSectionVisible(AppearanceSection, threshold))
                SetActiveCategoryButton(CatAppearanceBtn);
            else if (IsSectionVisible(PlaybackSection, threshold))
                SetActiveCategoryButton(CatPlaybackBtn);
            else
                SetActiveCategoryButton(CatFoldersBtn);
        }
        catch
        {
        }
    }

    private bool IsSectionVisible(FrameworkElement? element, double threshold)
    {
        if (element == null || SettingsScrollViewer == null) return false;
        var transform = element.TransformToVisual(SettingsScrollViewer);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        return point.Y <= threshold;
    }
}
