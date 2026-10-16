using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Reeled.Models;
using Reeled.Services;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }
    private Services.IThumbnailService? _thumbnailService;
    private bool _isWired;
    private GameClip? _hoveredClip;

    public HomePage()
    {
        ViewModel = App.GetService<HomeViewModel>();
        _thumbnailService = App.GetService<Services.IThumbnailService>();
        InitializeComponent();
        ViewModel.RequestScrollToTop += ScrollClipsToTop;
        ViewModel.ViewContentReplaced += OnViewContentReplaced;

        Helpers.CursorHelper.SetElementCursor(SidebarResizeHandle, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast));

        UpdateGridItemsSource();

        _sidebarWidth = ViewModel.SidebarWidth >= 200 ? ViewModel.SidebarWidth : 316.0;
        ApplyLocalization();

        Loaded += (s, e) =>
        {
            if (ViewModel.SidebarWidth > 0)
            {
                _sidebarWidth = ViewModel.SidebarWidth;
            }
            UpdateActiveIndicator(animate: false);
            AnimateSidebar(ViewModel.IsSidebarOpen, animate: false);
            UpdateLogo(ActualTheme);
            UpdateNavTabVisuals();
            ApplyLocalization();
            UpdateSortButtonUI();
            UpdateViewOptionsMenuUI();
            UpdateCardsMetadataVisibility();
            UpdateGridItemsSource();
            UpdateGridResponsiveLayout();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                UpdateGridResponsiveLayout();
            });
            UpdateUpdateIndicator(animate: false);

            // The page is cached, so Loaded fires again every time we come back from Settings.
            // Everything below subscribes handlers and must only run once.
            if (_isWired) return;
            _isWired = true;

            RegisterLibraryShortcuts();
            WireUpdateNotifications();

            if (ClipsGridView != null)
            {
                ClipsGridView.RequestedTheme = ActualTheme;
                ClipsGridView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnClipsGridPointerPressed), handledEventsToo: true);
                ClipsGridView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnClipsGridPointerMoved), handledEventsToo: true);
                ClipsGridView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnClipsGridPointerReleased), handledEventsToo: true);
                ClipsGridView.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnClipsGridPointerWheelChanged), handledEventsToo: true);
            }
            this.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);

            if (ClipsGridView != null)
            {
                EnsureScrollViewerHooked();
            }

            if (ClipsSearchBox != null)
            {
                ClipsSearchBox.Loaded += (s, e) => ApplyLocalization();
            }

            // Warm up and preload all animation types, DComp visuals, and Storyboard paths at idle priority
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PreloadAndWarmupAnimations);
        };

        try
        {
            var loc = App.GetService<Services.ILocalizationService>();
            if (loc != null)
            {
                loc.LanguageChanged += (s, e) => DispatcherQueue.TryEnqueue(ApplyLocalization);
            }
        }
        catch { }

        ActualThemeChanged += (s, e) =>
        {
            ApplyThemeVisuals(ActualTheme);
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.CurrentSection) || e.PropertyName == nameof(HomeViewModel.SelectedDirectory))
            {
                UpdateActiveIndicator(animate: true);
                UpdateNavTabVisuals();
            }
            else if (e.PropertyName == nameof(HomeViewModel.IsSidebarOpen) || e.PropertyName == nameof(HomeViewModel.IsSidebarCollapsed))
            {
                if (!_isUserToggling)
                {
                    AnimateSidebar(ViewModel.IsSidebarOpen, animate: true);
                }
            }
            else if (e.PropertyName == nameof(HomeViewModel.SidebarWidth))
            {
                _sidebarWidth = ViewModel.SidebarWidth;
                if (ViewModel.IsSidebarOpen && !_isResizingSidebar)
                {
                    SidebarContainer.Width = _sidebarWidth;
                    SidebarContentBorder.Width = _sidebarWidth;
                    SidebarSpacer.Width = _sidebarWidth;
                    _lastCalculatedWidth = -1;
                    _lastColumnCount = -1;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        UpdateGridResponsiveLayout();
                    });
                }
            }
            else if (e.PropertyName == nameof(HomeViewModel.SortIndex))
            {
                UpdateSortButtonUI();
                ScrollClipsToTop();
            }
            else if (e.PropertyName == nameof(HomeViewModel.ViewDensity))
            {
                UpdateViewOptionsMenuUI();
                _lastCalculatedWidth = -1;
                _lastColumnCount = -1;
                UpdateGridResponsiveLayout();
            }
            else if (e.PropertyName == nameof(HomeViewModel.DateGrouping))
            {
                UpdateGridItemsSource();
                UpdateViewOptionsMenuUI();
                _activeGroupWrapGrids.Clear();
                _lastCalculatedWidth = -1;
                _lastColumnCount = -1;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    UpdateGridResponsiveLayout();
                });
            }
            else if (e.PropertyName == nameof(HomeViewModel.MetadataHoverOnly))
            {
                UpdateViewOptionsMenuUI();
                UpdateCardsMetadataVisibility();
            }
            else if (e.PropertyName == nameof(HomeViewModel.ShowSkeletonLoading) || e.PropertyName == nameof(HomeViewModel.IsLoading))
            {
                UpdateSkeletonAnimations();
                if (!ViewModel.ShowSkeletonLoading)
                {
                    _clipsScrollViewer = null;
                    _activeGroupWrapGrids.Clear();
                    _lastCalculatedWidth = -1;
                    _lastColumnCount = -1;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        UpdateGridResponsiveLayout();
                        ScrollClipsToTop();
                    });
                }
            }
        };
    }

    public void ApplyThemeVisuals(ElementTheme theme)
    {
        this.RequestedTheme = theme;
        UpdateLogo(theme);
        UpdateNavTabVisuals();

        if (ClipsSearchBox != null)
        {
            ClipsSearchBox.RequestedTheme = theme;
        }

        if (SortOptionsButton != null)
        {
            SortOptionsButton.RequestedTheme = theme;
        }

        if (ViewOptionsButton != null)
        {
            ViewOptionsButton.RequestedTheme = theme;
        }

        if (SortMenuFlyout != null)
        {
            SortMenuFlyout.XamlRoot = this.XamlRoot;
            foreach (var item in SortMenuFlyout.Items)
            {
                if (item is FrameworkElement fe) fe.RequestedTheme = theme;
            }
        }

        if (ViewMenuFlyout != null)
        {
            ViewMenuFlyout.XamlRoot = this.XamlRoot;
            foreach (var item in ViewMenuFlyout.Items)
            {
                if (item is FrameworkElement fe) fe.RequestedTheme = theme;
            }
        }

        if (DirectoriesTreeView != null)
        {
            DirectoriesTreeView.RequestedTheme = theme;
            UpdateTreeViewItemsTheme(DirectoriesTreeView, theme);
        }

        if (ClipsGridView != null)
        {
            ClipsGridView.RequestedTheme = theme;
            UpdateRealizedCardThemes(theme);
        }
    }

    private void UpdateTreeViewItemsTheme(DependencyObject parent, ElementTheme theme)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe)
            {
                fe.RequestedTheme = theme;
            }
            UpdateTreeViewItemsTheme(child, theme);
        }
    }

    private void UpdateRealizedCardThemes(ElementTheme theme)
    {
        if (ClipsGridView == null) return;

        void UpdateContainer(object item)
        {
            if (ClipsGridView.ContainerFromItem(item) is GridViewItem gvi)
            {
                gvi.RequestedTheme = theme;
                if (FindVisualChildByName<FrameworkElement>(gvi, "CardRootGrid") is FrameworkElement rootGrid)
                {
                    rootGrid.RequestedTheme = theme;
                }
                if (FindVisualChildByName<Border>(gvi, "HoverHighlightBorder") is Border hoverBorder)
                {
                    hoverBorder.RequestedTheme = theme;
                }
            }
        }

        if (ViewModel.DateGrouping == DateGroupingMode.None)
        {
            foreach (var item in ViewModel.FilteredClips)
            {
                UpdateContainer(item);
            }
        }
        else
        {
            foreach (var group in ViewModel.GroupedClips)
            {
                foreach (var item in group)
                {
                    UpdateContainer(item);
                }
            }
        }
    }

    private Storyboard? _viewSwitchStoryboard;
    private string? _lastViewKey;

    private void OnViewContentReplaced()
    {
        if (ViewModel.DateGrouping != DateGroupingMode.None)
        {
            RebindGroupedSource();
        }

        // Only animate real view switches; a background rescan of the same view should stay still
        string viewKey = $"{ViewModel.CurrentSection}|{ViewModel.SelectedDirectory?.FullPath}";
        if (viewKey != _lastViewKey && _lastViewKey != null)
        {
            PlayViewSwitchTransition();
        }
        _lastViewKey = viewKey;
    }

    /// <summary>
    /// Group headers are realized per CollectionViewSource view. Replacing every group through a
    /// collection Reset can leave header containers unrealized, so a section change hands the grid a
    /// fresh view instead. Item containers are virtualized, so this costs one cheap re-realization.
    /// </summary>
    private void RebindGroupedSource()
    {
        if (ClipsGridView == null || ClipsGroupedSource == null) return;

        // Rebinding discards containers without a recycle pass, so release bitmaps that are leaving the view
        var visible = new HashSet<GameClip>(ViewModel.FilteredClips);
        foreach (var clip in ViewModel.AllClips)
        {
            if (clip.Thumbnail != null && !visible.Contains(clip))
            {
                clip.Thumbnail = null;
            }
        }

        ClipsGroupedSource.Source = null;
        ClipsGroupedSource.Source = ViewModel.GroupedClips;
        ClipsGridView.ItemsSource = ClipsGroupedSource.View;

        _activeGroupWrapGrids.Clear();
        _lastCalculatedWidth = -1;
        _lastColumnCount = -1;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => UpdateGridResponsiveLayout());
    }

    private void PlayViewSwitchTransition()
    {
        if (ClipsHost == null || ClipsHostTranslate == null) return;

        _viewSwitchStoryboard?.Stop();
        if (IsReduceMotionEnabled())
        {
            ClipsHost.Opacity = 1.0;
            ClipsHostTranslate.Y = 0.0;
            if (HeaderTitlePanel != null) HeaderTitlePanel.Opacity = 1.0;
            return;
        }

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var sb = new Storyboard();

        var fade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = ease };
        Storyboard.SetTarget(fade, ClipsHost);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        var rise = new DoubleAnimation { From = 14.0, To = 0.0, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease };
        Storyboard.SetTarget(rise, ClipsHostTranslate);
        Storyboard.SetTargetProperty(rise, "Y");
        sb.Children.Add(rise);

        if (HeaderTitlePanel != null)
        {
            var titleFade = new DoubleAnimation { From = 0.35, To = 1.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = ease };
            Storyboard.SetTarget(titleFade, HeaderTitlePanel);
            Storyboard.SetTargetProperty(titleFade, "Opacity");
            sb.Children.Add(titleFade);
        }

        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_viewSwitchStoryboard, sb)) return;
            ClipsHost.Opacity = 1.0;
            ClipsHostTranslate.Y = 0.0;
            if (HeaderTitlePanel != null) HeaderTitlePanel.Opacity = 1.0;
        };

        _viewSwitchStoryboard = sb;
        sb.Begin();
    }

    public void OnReturnedFromViewer()
    {
        _activeGroupWrapGrids.RemoveWhere(wg => wg.XamlRoot == null);
        FindAndRegisterGroupWrapGrids(ClipsGridView);

        _lastCalculatedWidth = -1;
        _lastColumnCount = -1;
        UpdateGridResponsiveLayout();

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ClipsGridView == null) return;
            FindAndRegisterGroupWrapGrids(ClipsGridView);
            UpdateGridResponsiveLayout();
        });
    }

    private void ApplyLocalization()
    {
        try
        {
            var loc = App.GetService<Services.ILocalizationService>();
            if (loc == null) return;

            if (HomeNavText != null) HomeNavText.Text = loc["Nav_Home"];
            if (FavoritesNavText != null) FavoritesNavText.Text = loc["Nav_Favorites"];
            if (SavedMomentsNavText != null) SavedMomentsNavText.Text = loc["Nav_SavedMoments"];
            if (OpenFileNavText != null) OpenFileNavText.Text = loc["Nav_OpenFile"];
            if (FoldersHeaderNavText != null) FoldersHeaderNavText.Text = loc["Nav_Folders"];
            if (FoldersAddNavText != null) FoldersAddNavText.Text = loc["Nav_AddFolder"];
            if (SettingsNavText != null) SettingsNavText.Text = loc["Nav_Settings"];

            // Tooltips
            if (CollapseSidebarButton != null) ToolTipService.SetToolTip(CollapseSidebarButton, loc["Tooltip_CollapseSidebar"]);
            if (ShowSidebarButton != null) ToolTipService.SetToolTip(ShowSidebarButton, loc["Tooltip_ShowSidebar"]);
            if (AddWatchFolderButton != null) ToolTipService.SetToolTip(AddWatchFolderButton, loc["Tooltip_AddWatchFolder"]);
            if (OpenFileNavButton != null) ToolTipService.SetToolTip(OpenFileNavButton, loc["Tooltip_OpenFile"]);
            if (RefreshClipsButton != null) ToolTipService.SetToolTip(RefreshClipsButton, loc["Tooltip_RefreshClips"]);
            if (ExplorerHyperlinkButton != null) ToolTipService.SetToolTip(ExplorerHyperlinkButton, loc["Tooltip_OpenInExplorer"]);

            // Search Box Placeholder
            if (ClipsSearchBox != null)
            {
                string searchPh = loc["Search_Placeholder"];
                ClipsSearchBox.PlaceholderText = searchPh;
                var innerTextBox = FindDescendant<TextBox>(ClipsSearchBox);
                if (innerTextBox != null)
                {
                    innerTextBox.PlaceholderText = searchPh;
                }
            }

            // Sort Menu Items
            if (SortNewestItem != null) SortNewestItem.Text = loc["Sort_NewestDate"];
            if (SortOldestItem != null) SortOldestItem.Text = loc["Sort_OldestDate"];
            if (SortNameAZItem != null) SortNameAZItem.Text = loc["Sort_TitleAZ"];
            if (SortNameZAItem != null) SortNameZAItem.Text = loc["Sort_TitleZA"];
            if (SortDurationItem != null) SortDurationItem.Text = loc["Sort_Duration"];
            if (SortFileSizeItem != null) SortFileSizeItem.Text = loc["Sort_FileSize"];

            // View Options Menu Items
            if (ViewOptionsText != null) ViewOptionsText.Text = loc["View_Options"];
            if (ViewDensityHeader != null) ViewDensityHeader.Text = loc["View_Density"];
            if (ViewDensityComfortableItem != null) ViewDensityComfortableItem.Text = loc["View_Density_Comfortable"];
            if (ViewDensityCompactItem != null) ViewDensityCompactItem.Text = loc["View_Density_Compact"];
            if (ViewDensityLargeItem != null) ViewDensityLargeItem.Text = loc["View_Density_Large"];

            if (ViewGroupingHeader != null) ViewGroupingHeader.Text = loc["View_Grouping"];
            if (ViewGroupingNoneItem != null) ViewGroupingNoneItem.Text = loc["View_Grouping_None"];
            if (ViewGroupingDayItem != null) ViewGroupingDayItem.Text = loc["View_Grouping_Day"];
            if (ViewGroupingMonthItem != null) ViewGroupingMonthItem.Text = loc["View_Grouping_Month"];
            if (ViewGroupingYearItem != null) ViewGroupingYearItem.Text = loc["View_Grouping_Year"];

            if (ViewMetadataHoverItem != null) ViewMetadataHoverItem.Text = loc["View_MetadataHoverOnly"];

            UpdateSortButtonUI();
            UpdateViewOptionsMenuUI();

            // Folders Empty Fallback Prompt
            if (FoldersFallbackTitleText != null) FoldersFallbackTitleText.Text = loc["Folders_FallbackTitle"];
            if (FoldersFallbackSubtitleText != null) FoldersFallbackSubtitleText.Text = loc["Folders_FallbackSubtitle"];
            if (FoldersFallbackButton != null) FoldersFallbackButton.Content = loc["Folders_ChooseFolder"];
            if (EmptyStateOpenFileText != null) EmptyStateOpenFileText.Text = loc["Empty_OpenFileButton"];
        }
        catch { }
    }

    private bool _isAnimationsWarmedUp;

    private void PreloadAndWarmupAnimations()
    {
        if (_isAnimationsWarmedUp) return;
        _isAnimationsWarmedUp = true;

        try
        {
            // 1. Prime the DirectComposition visual tree for key interactive surfaces
            // This prevents first-time DComp visual-peer allocation hitch
            if (ActiveIndicatorPill != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ActiveIndicatorPill);
            if (SidebarContainer != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SidebarContainer);
            if (SidebarContentBorder != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SidebarContentBorder);
            if (DirectoriesTreeView != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(DirectoriesTreeView);
            if (HomeNavButton != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(HomeNavButton);
            if (FavoritesNavButton != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FavoritesNavButton);
            if (SavedMomentsNavButton != null)
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SavedMomentsNavButton);

            // 2. Instantiate and pre-JIT all animation types, easings, and composition primitives
            var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };
            var easeInOut = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            var easeIn = new QuarticEase { EasingMode = EasingMode.EaseIn };
            var cubicEaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var cubicEaseInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var cubicEaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        }
        catch { }
    }

    private void UpdateLogo(ElementTheme theme)
    {
        if (SidebarLogoSvg == null) return;
        bool isLight = (theme == ElementTheme.Light);
        var uri = isLight 
            ? new System.Uri("ms-appx:///Assets/dark-banner.svg") 
            : new System.Uri("ms-appx:///Assets/light-banner.svg");
        if (SidebarLogoSvg.UriSource != uri)
        {
            SidebarLogoSvg.UriSource = uri;
        }
    }

    private void UpdateNavTabVisuals(bool animate = true)
    {
        UpdateNavButtonState(HomeNavButton, HomeSelectedBg, HomeNavIcon, HomeNavText, ViewModel.IsHomeSelected, animate);
        UpdateNavButtonState(FavoritesNavButton, FavoritesSelectedBg, FavoritesNavIcon, FavoritesNavText, ViewModel.IsFavoritesSelected, animate);
        UpdateNavButtonState(SavedMomentsNavButton, SavedMomentsSelectedBg, SavedMomentsNavIcon, SavedMomentsNavText, ViewModel.IsSavedMomentsSelected, animate);
    }

    private void UpdateNavButtonState(Button? btn, Border? selBg, FontIcon? icon, TextBlock? text, bool isSelected, bool animate = true)
    {
        if (btn == null || icon == null || text == null) return;

        bool isLight = (ActualTheme == ElementTheme.Light);
        double targetOpacity = isSelected ? 1.0 : 0.0;

        if (selBg != null)
        {
            if (!animate || Math.Abs(selBg.Opacity - targetOpacity) < 0.01)
            {
                selBg.Opacity = targetOpacity;
            }
            else
            {
                var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(isSelected ? 180 : 140),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuarticEase
                    {
                        EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                    }
                };
                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sb.Children.Add(anim);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, selBg);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
                Helpers.Motion.Begin(sb);
            }
        }

        Brush foreground = isSelected
            ? (isLight ? NavSelectedLightBrush : NavSelectedDarkBrush)
            : (isLight ? NavIdleLightBrush : NavIdleDarkBrush);

        icon.Foreground = foreground;
        text.Foreground = foreground;
        text.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }

    private static readonly SolidColorBrush NavSelectedLightBrush = new(Windows.UI.Color.FromArgb(255, 20, 20, 20));
    private static readonly SolidColorBrush NavSelectedDarkBrush = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush NavIdleLightBrush = new(Windows.UI.Color.FromArgb(255, 70, 70, 75));
    private static readonly SolidColorBrush NavIdleDarkBrush = new(Windows.UI.Color.FromArgb(255, 210, 210, 210));

    private double _currentIndicatorY = 48;
    private double _targetIndicatorY = 48;
    private bool _isIndicatorVisible = true;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _indicatorStoryboard;
    private ScrollViewer? _treeViewScrollViewer;

    private enum ActiveSectionKind
    {
        None,
        TopNav,
        Folders
    }
    private ActiveSectionKind _currentActiveSection = ActiveSectionKind.TopNav;

    private bool IsReduceMotionEnabled() => Helpers.Motion.IsReduced;

    private void UpdateActiveIndicator(bool animate = true)
    {
        if (IsReduceMotionEnabled()) animate = false;
        if (ActiveIndicatorPill == null || IndicatorTranslation == null || IndicatorScale == null)
            return;

        ActiveSectionKind newSection = ActiveSectionKind.None;
        if (ViewModel.IsHomeSelected || ViewModel.IsFavoritesSelected || ViewModel.IsSavedMomentsSelected)
        {
            newSection = ActiveSectionKind.TopNav;
        }
        else if (ViewModel.CurrentSection == NavigationSection.Folder && ViewModel.SelectedDirectory != null && IsNodeVisibleInTree(ViewModel.SelectedDirectory))
        {
            newSection = ActiveSectionKind.Folders;
        }

        bool isCrossSection = (_currentActiveSection != ActiveSectionKind.None &&
                               newSection != ActiveSectionKind.None &&
                               _currentActiveSection != newSection);
        _currentActiveSection = newSection;

        if (ViewModel.IsHomeSelected)
        {
            double targetY = GetTargetIndicatorY(HomeNavButton, 48);
            if (isCrossSection && animate)
                AnimateCrossSectionTransition(targetY);
            else
                AnimateIndicatorTo(targetY, animate);
        }
        else if (ViewModel.IsFavoritesSelected)
        {
            double targetY = GetTargetIndicatorY(FavoritesNavButton, 89);
            if (isCrossSection && animate)
                AnimateCrossSectionTransition(targetY);
            else
                AnimateIndicatorTo(targetY, animate);
        }
        else if (ViewModel.IsSavedMomentsSelected)
        {
            double targetY = GetTargetIndicatorY(SavedMomentsNavButton, 130);
            if (isCrossSection && animate)
                AnimateCrossSectionTransition(targetY);
            else
                AnimateIndicatorTo(targetY, animate);
        }
        else if (ViewModel.CurrentSection == NavigationSection.Folder && ViewModel.SelectedDirectory != null)
        {
            HookTreeViewScrollViewer();
            var node = ViewModel.SelectedDirectory;
            if (!IsNodeVisibleInTree(node))
            {
                AnimateIndicatorVisibility(false, animate);
                return;
            }

            if (DirectoriesTreeView?.ContainerFromItem(node) is TreeViewItem container)
            {
                if (IsContainerVisibleInTreeView(container))
                {
                    double targetY = GetTargetIndicatorY(container, _currentIndicatorY);
                    if (isCrossSection && animate)
                        AnimateCrossSectionTransition(targetY);
                    else
                        AnimateIndicatorTo(targetY, animate);
                }
                else
                {
                    AnimateIndicatorVisibility(false, animate);
                }
            }
            else
            {
                // Node is logically expanded, but container is pending layout pass; hide indicator so no ghost remains
                AnimateIndicatorVisibility(false, animate);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ViewModel.CurrentSection == NavigationSection.Folder && ViewModel.SelectedDirectory == node)
                    {
                        if (IsNodeVisibleInTree(node) && DirectoriesTreeView?.ContainerFromItem(node) is TreeViewItem delayedContainer && delayedContainer.ActualHeight > 0)
                        {
                            if (IsContainerVisibleInTreeView(delayedContainer))
                            {
                                double targetY = GetTargetIndicatorY(delayedContainer, _currentIndicatorY);
                                if (isCrossSection && animate)
                                    AnimateCrossSectionTransition(targetY);
                                else
                                    AnimateIndicatorTo(targetY, animate);
                            }
                        }
                    }
                });
            }
        }
        else
        {
            AnimateIndicatorVisibility(false, animate);
        }
    }

    private void AnimateCrossSectionTransition(double targetY)
    {
        _targetIndicatorY = targetY;

        if (_indicatorStoryboard != null)
        {
            _indicatorStoryboard.Stop();
            _indicatorStoryboard = null;
        }

        if (!_isIndicatorVisible)
        {
            _currentIndicatorY = targetY;
            IndicatorTranslation.Y = targetY;
            AnimateIndicatorTo(targetY, animate: true);
            return;
        }

        var fadeOutSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var easeIn = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn };
        var animFadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = easeIn
        };
        var animScaleDown = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = easeIn
        };
        fadeOutSb.Children.Add(animFadeOut);
        fadeOutSb.Children.Add(animScaleDown);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animFadeOut, ActiveIndicatorPill);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animFadeOut, "Opacity");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleDown, IndicatorScale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleDown, "ScaleY");

        fadeOutSb.Completed += (s, e) =>
        {
            _currentIndicatorY = targetY;
            IndicatorTranslation.Y = targetY;
            ActiveIndicatorPill.Opacity = 0.0;
            IndicatorScale.ScaleY = 0.0;

            var fadeInSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var easeOut = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
            var animFadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = easeOut
            };
            var animScaleUp = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = easeOut
            };
            fadeInSb.Children.Add(animFadeIn);
            fadeInSb.Children.Add(animScaleUp);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animFadeIn, ActiveIndicatorPill);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animFadeIn, "Opacity");
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleUp, IndicatorScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleUp, "ScaleY");

            fadeInSb.Completed += (s2, e2) =>
            {
                ActiveIndicatorPill.Opacity = 1.0;
                IndicatorScale.ScaleY = 1.0;
                _isIndicatorVisible = true;
            };

            _indicatorStoryboard = fadeInSb;
            Helpers.Motion.Begin(fadeInSb);
        };

        _indicatorStoryboard = fadeOutSb;
        Helpers.Motion.Begin(fadeOutSb);
    }

    private bool IsNodeVisibleInTree(DirectoryNode target)
    {
        if (target == null) return false;
        foreach (var root in ViewModel.Directories)
        {
            if (root == target) return true;
            if (IsNodeVisibleRecursive(root, target)) return true;
        }
        return false;
    }

    private static bool IsNodeVisibleRecursive(DirectoryNode current, DirectoryNode target)
    {
        if (!current.IsExpanded) return false;
        foreach (var child in current.SubDirectories)
        {
            if (child == target) return true;
            if (IsNodeVisibleRecursive(child, target)) return true;
        }
        return false;
    }

    private double GetTargetIndicatorY(FrameworkElement targetElement, double fallbackY)
    {
        try
        {
            if (targetElement != null && SidebarRootGrid != null && targetElement.ActualHeight > 0 && SidebarRootGrid.ActualHeight > 0)
            {
                FrameworkElement rowElement = targetElement;
                if (targetElement is TreeViewItem tvi)
                {
                    rowElement = FindVisualChildByName<FrameworkElement>(tvi, "ContentPresenterGrid") ?? tvi;
                }

                var transform = rowElement.TransformToVisual(SidebarRootGrid);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                double animationOffsetY = 0.0;
                if (targetElement.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
                {
                    animationOffsetY = tt.Y;
                }

                double rowHeight = (rowElement.ActualHeight > 0 && rowElement.ActualHeight < 60.0)
                    ? rowElement.ActualHeight
                    : 36.0;
                double pillHeight = ActiveIndicatorPill.ActualHeight > 0 ? ActiveIndicatorPill.ActualHeight : 16.0;

                return (point.Y - animationOffsetY) + (rowHeight - pillHeight) / 2.0;
            }
        }
        catch { }
        return fallbackY;
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

    private bool IsContainerVisibleInTreeView(FrameworkElement container)
    {
        if (DirectoriesTreeView == null || container.ActualHeight <= 0) return false;
        if (container.Visibility != Visibility.Visible) return false;
        try
        {
            var transform = container.TransformToVisual(DirectoriesTreeView);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double pillTop = pt.Y + 10.0;
            double pillBottom = pillTop + 16.0;
            return pillBottom > 0 && pillTop < DirectoriesTreeView.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateFolderIndicatorPosition()
    {
        if (ViewModel.CurrentSection != NavigationSection.Folder || ViewModel.SelectedDirectory == null)
            return;

        var node = ViewModel.SelectedDirectory;
        if (!IsNodeVisibleInTree(node))
        {
            if (_isIndicatorVisible)
            {
                ActiveIndicatorPill.Opacity = 0.0;
                IndicatorScale.ScaleY = 0.0;
                _isIndicatorVisible = false;
            }
            return;
        }

        if (DirectoriesTreeView?.ContainerFromItem(node) is TreeViewItem container)
        {
            if (IsContainerVisibleInTreeView(container))
            {
                if (_indicatorStoryboard != null)
                {
                    _indicatorStoryboard.Stop();
                    _indicatorStoryboard = null;
                }

                double targetY = GetTargetIndicatorY(container, _currentIndicatorY);
                _currentIndicatorY = targetY;
                _targetIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                if (!_isIndicatorVisible)
                {
                    ActiveIndicatorPill.Opacity = 1.0;
                    IndicatorScale.ScaleY = 1.0;
                    _isIndicatorVisible = true;
                }
            }
            else
            {
                if (_isIndicatorVisible)
                {
                    ActiveIndicatorPill.Opacity = 0.0;
                    IndicatorScale.ScaleY = 0.0;
                    _isIndicatorVisible = false;
                }
            }
        }
        else
        {
            if (_isIndicatorVisible)
            {
                ActiveIndicatorPill.Opacity = 0.0;
                IndicatorScale.ScaleY = 0.0;
                _isIndicatorVisible = false;
            }
        }
    }

    private void HookTreeViewScrollViewer()
    {
        if (_treeViewScrollViewer != null) return;
        if (DirectoriesTreeView == null) return;

        _treeViewScrollViewer = FindVisualChild<ScrollViewer>(DirectoriesTreeView);
        if (_treeViewScrollViewer != null)
        {
            _treeViewScrollViewer.ViewChanged += (s, args) => UpdateFolderIndicatorPosition();
            _treeViewScrollViewer.ViewChanging += (s, args) => UpdateFolderIndicatorPosition();
        }
    }

    private void OnDirectoriesTreeViewLoaded(object sender, RoutedEventArgs e)
    {
        HookTreeViewScrollViewer();
        if (DirectoriesTreeView != null && !_isTreeViewWired)
        {
            _isTreeViewWired = true;
            DirectoriesTreeView.LayoutUpdated += OnDirectoriesTreeViewLayoutUpdated;
            DirectoriesTreeView.PointerWheelChanged += (s, args) => UpdateFolderIndicatorPosition();
        }
    }

    private bool _isTreeViewWired;

    private void OnDirectoriesTreeViewLayoutUpdated(object? sender, object e)
    {
        HookTreeViewScrollViewer();
        if (_treeViewScrollViewer != null && DirectoriesTreeView != null)
        {
            DirectoriesTreeView.LayoutUpdated -= OnDirectoriesTreeViewLayoutUpdated;
        }
    }

    private void OnTreeViewItemLoading(FrameworkElement sender, object args)
    {
        var tvi = sender as TreeViewItem;
        var node = (tvi?.DataContext as DirectoryNode) ?? (tvi?.Content as DirectoryNode);
        if (node == null || tvi == null) return;

        // Containers are recycled: never let one keep the faded state of a row it showed before
        ResetFolderRow(tvi);

        if (!IsReduceMotionEnabled() && IsInsideExpandingBranch(node))
        {
            AnimateFolderRowIn(tvi, _expandOrder.TryGetValue(node, out int order) ? order : 0);
        }

        if (node.HasSubDirectories)
        {
            VisualStateManager.GoToState(tvi, node.IsExpanded ? "Expanded" : "Collapsed", false);
        }
    }

    private static void FindAllVisualChildren<T>(DependencyObject parent, System.Collections.Generic.List<T> results) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                results.Add(typed);
            }
            FindAllVisualChildren(child, results);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }

    private void AnimateIndicatorTo(double targetY, bool animate)
    {
        if (!_isIndicatorVisible)
        {
            if (_indicatorStoryboard != null)
            {
                _indicatorStoryboard.Stop();
                _indicatorStoryboard = null;
            }
            _targetIndicatorY = targetY;
            _currentIndicatorY = targetY;
            IndicatorTranslation.Y = targetY;

            if (!animate)
            {
                IndicatorScale.ScaleY = 1.0;
                ActiveIndicatorPill.Opacity = 1.0;
                _isIndicatorVisible = true;
                return;
            }

            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

            var animOpacity = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = ease
            };
            var animScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = ease
            };

            sb.Children.Add(animOpacity);
            sb.Children.Add(animScaleY);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, ActiveIndicatorPill);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleY, IndicatorScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleY, "ScaleY");

            sb.Completed += (s, e) =>
            {
                ActiveIndicatorPill.Opacity = 1.0;
                IndicatorScale.ScaleY = 1.0;
            };

            _indicatorStoryboard = sb;
            Helpers.Motion.Begin(sb);
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
            IndicatorScale.ScaleY = 1.0;
        }

        if (!animate)
        {
            _currentIndicatorY = targetY;
            IndicatorTranslation.Y = targetY;
            IndicatorScale.ScaleY = 1.0;
            ActiveIndicatorPill.Opacity = 1.0;
            return;
        }

        double distance = Math.Abs(targetY - fromY);
        var moveSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var appleEase = new Microsoft.UI.Xaml.Media.Animation.QuarticEase
        {
            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
        };

        // 1. Vertical Glide Animation (Y translation) with Apple fluid decelerate
        var animTranslate = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromY,
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = appleEase
        };
        moveSb.Children.Add(animTranslate);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animTranslate, IndicatorTranslation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animTranslate, "Y");

        // 2. Subtle fluid stretch
        if (distance > 5.0)
        {
            double stretch = Math.Min(1.15, 1.0 + (distance / 500.0));
            var animScaleKeyFrames = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
            });
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                Value = stretch,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                EasingFunction = appleEase
            });
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                EasingFunction = appleEase
            });

            moveSb.Children.Add(animScaleKeyFrames);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleKeyFrames, IndicatorScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleKeyFrames, "ScaleY");
        }

        moveSb.Completed += (s, e) =>
        {
            IndicatorTranslation.Y = targetY;
            IndicatorScale.ScaleY = 1.0;
            _currentIndicatorY = targetY;
        };

        _currentIndicatorY = targetY;
        _indicatorStoryboard = moveSb;
        Helpers.Motion.Begin(moveSb);
    }

    private void AnimateIndicatorVisibility(bool isVisible, bool animate)
    {
        if (_isIndicatorVisible == isVisible) return;

        if (_indicatorStoryboard != null)
        {
            _indicatorStoryboard.Stop();
            _indicatorStoryboard = null;
            IndicatorTranslation.Y = _currentIndicatorY;
            IndicatorScale.ScaleY = 1.0;
        }

        if (!isVisible)
        {
            _targetIndicatorY = -1;
        }

        if (!animate)
        {
            ActiveIndicatorPill.Opacity = isVisible ? 1.0 : 0.0;
            IndicatorScale.ScaleY = isVisible ? 1.0 : 0.0;
            _isIndicatorVisible = isVisible;
            return;
        }

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
        {
            EasingMode = isVisible ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn
        };

        var animOpacity = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = isVisible ? 1.0 : 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = ease
        };
        var animScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = isVisible ? 1.0 : 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = ease
        };

        sb.Children.Add(animOpacity);
        sb.Children.Add(animScaleY);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, ActiveIndicatorPill);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleY, IndicatorScale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleY, "ScaleY");

        sb.Completed += (s, e) =>
        {
            ActiveIndicatorPill.Opacity = isVisible ? 1.0 : 0.0;
            IndicatorScale.ScaleY = isVisible ? 1.0 : 0.0;
        };

        _indicatorStoryboard = sb;
        Helpers.Motion.Begin(sb);
        _isIndicatorVisible = isVisible;
    }

    private Storyboard? _sidebarStoryboard;
    private double _sidebarWidth = 316.0;
    private bool _wasAutoCollapsed;
    private bool _isUserToggling;
    private bool _isResizingSidebar;
    private double _resizeStartPointerX;
    private double _resizeStartWidth;

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double scale = XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0) scale = 1.0;

        // Minimum window width constraint is 760 DIPs.
        // Scale-aware threshold: higher display scaling consumes more physical pixels and layout space,
        // so collapse threshold scales proportionally (e.g. 860 DIPs at 100%, 910 DIPs at 150%).
        double collapseThreshold = 760.0 + (100.0 * scale);
        double restoreThreshold = collapseThreshold + 30.0; // 30px hysteresis to prevent rapid toggling

        if (e.NewSize.Width <= collapseThreshold)
        {
            if (ViewModel.IsSidebarOpen && !_isUserToggling)
            {
                _wasAutoCollapsed = true;
                ViewModel.IsSidebarCollapsed = true;
                (App.Window as MainWindow)?.SetSidebarOpen(false);
                AnimateSidebar(isOpen: false, animate: false);
            }
        }
        else if (e.NewSize.Width >= restoreThreshold)
        {
            if (_wasAutoCollapsed && ViewModel.IsSidebarCollapsed && !_isUserToggling)
            {
                _wasAutoCollapsed = false;
                ViewModel.IsSidebarCollapsed = false;
                (App.Window as MainWindow)?.SetSidebarOpen(true);
                AnimateSidebar(isOpen: true, animate: false);
            }
        }
    }

    private void AnimateSidebar(bool isOpen, bool animate = true)
    {
        if (IsReduceMotionEnabled()) animate = false;
        _sidebarStoryboard?.Stop();
        _sidebarStoryboard = null;

        double width = _sidebarWidth;
        SidebarContainer.Width = width;
        SidebarContentBorder.Width = width;

        if (App.Window is MainWindow mw)
        {
            if (isOpen && animate) mw.EnsureWidthForSidebar(width);
            mw.SetSidebarOpen(isOpen);
        }

        // Where things are on screen right now, so an interrupted toggle continues from there
        double fromSidebarX = SidebarContainer.Visibility == Visibility.Visible ? SidebarTranslate.X : -width;
        double contentVisualX = SidebarSpacer.Width + MainContentTranslate.X;

        // Layout changes exactly once, up front: the spacer snaps to its final width and the clip grid
        // reflows to its final column count immediately. Everything that moves afterwards is a
        // render transform, which the compositor animates without any layout work.
        double targetSpacer = isOpen ? width : 0.0;
        SidebarSpacer.Width = targetSpacer;
        _lastCalculatedWidth = -1;
        _lastColumnCount = -1;
        UpdateGridResponsiveLayout(forcedGridWidth: Math.Max(0, ActualWidth - targetSpacer));

        double targetSidebarX = isOpen ? 0.0 : -width;
        SidebarContainer.Visibility = Visibility.Visible;

        if (ShowSidebarContainer != null)
        {
            ShowSidebarContainer.Width = isOpen ? 0.0 : 42.0;
            ShowSidebarContainer.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        }

        if (!animate)
        {
            SidebarTranslate.X = targetSidebarX;
            MainContentTranslate.X = 0.0;
            SidebarContentBorder.Opacity = 1.0;
            if (ShowSidebarContainer != null) ShowSidebarContainer.Opacity = isOpen ? 0.0 : 1.0;
            SidebarContainer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        // Keep the content visually where it was, then let it glide into its new slot with the sidebar
        MainContentTranslate.X = contentVisualX - targetSpacer;
        SidebarTranslate.X = fromSidebarX;

        var duration = TimeSpan.FromMilliseconds(isOpen ? 260 : 220);
        EasingFunctionBase ease = isOpen
            ? new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };

        var sb = new Storyboard();

        var slide = new DoubleAnimation { To = targetSidebarX, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(slide, SidebarTranslate);
        Storyboard.SetTargetProperty(slide, "X");
        sb.Children.Add(slide);

        var contentSlide = new DoubleAnimation { To = 0.0, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(contentSlide, MainContentTranslate);
        Storyboard.SetTargetProperty(contentSlide, "X");
        sb.Children.Add(contentSlide);

        if (ShowSidebarContainer != null)
        {
            var buttonFade = new DoubleAnimation
            {
                To = isOpen ? 0.0 : 1.0,
                Duration = TimeSpan.FromMilliseconds(isOpen ? 120 : 200),
                EasingFunction = ease
            };
            Storyboard.SetTarget(buttonFade, ShowSidebarContainer);
            Storyboard.SetTargetProperty(buttonFade, "Opacity");
            sb.Children.Add(buttonFade);
        }

        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_sidebarStoryboard, sb)) return;
            _sidebarStoryboard = null;
            SidebarTranslate.X = targetSidebarX;
            MainContentTranslate.X = 0.0;
            if (!isOpen)
            {
                SidebarContainer.Visibility = Visibility.Collapsed;
            }
        };

        _sidebarStoryboard = sb;
        sb.Begin();
    }

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        _isUserToggling = true;
        _wasAutoCollapsed = false; // User manually chose state
        ViewModel.ToggleSidebar();
        AnimateSidebar(ViewModel.IsSidebarOpen, animate: true);
        _isUserToggling = false;
    }

    private void OnSidebarResizeHandlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ResizeHighlightLine != null)
        {
            ResizeHighlightLine.Opacity = 0.6;
        }
    }

    private void OnSidebarResizeHandlePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingSidebar)
        {
            this.ProtectedCursor = null;
            if (ResizeHighlightLine != null)
            {
                ResizeHighlightLine.Opacity = 0.0;
            }
        }
    }

    private void OnSidebarResizeHandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            el.CapturePointer(e.Pointer);
            _isResizingSidebar = true;
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            _resizeStartPointerX = e.GetCurrentPoint(this).Position.X;
            _resizeStartWidth = _sidebarWidth;
            if (ResizeHighlightLine != null)
            {
                ResizeHighlightLine.Opacity = 1.0;
            }
            e.Handled = true;
        }
    }

    private void OnSidebarResizeHandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isResizingSidebar)
        {
            double currentX = e.GetCurrentPoint(this).Position.X;
            double delta = currentX - _resizeStartPointerX;
            double newWidth = Math.Clamp(_resizeStartWidth + delta, 220.0, 500.0);

            _sidebarWidth = newWidth;
            SidebarSpacer.Width = newWidth;
            SidebarContainer.Width = newWidth;
            SidebarContentBorder.Width = newWidth;
            ViewModel.SidebarWidth = newWidth;

            e.Handled = true;
        }
    }

    private void OnSidebarResizeHandlePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isResizingSidebar)
        {
            _isResizingSidebar = false;
            this.ProtectedCursor = null;
            if (sender is UIElement el)
            {
                el.ReleasePointerCapture(e.Pointer);
            }
            if (ResizeHighlightLine != null)
            {
                ResizeHighlightLine.Opacity = 0.0;
            }
            ViewModel.SaveSidebarWidth(_sidebarWidth);
            e.Handled = true;
        }
    }

    private void OnSidebarResizeHandlePointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        OnSidebarResizeHandlePointerReleased(sender, e);
    }

    private double _currentCardWidth = 280;
    private double _currentCardHeight = 158;
    private double _lastCalculatedWidth = -1;
    private int _lastColumnCount = -1;
    private readonly System.Collections.Generic.HashSet<FrameworkElement> _activeCardGrids = new();
    private readonly System.Collections.Generic.HashSet<ItemsWrapGrid> _activeGroupWrapGrids = new();

    private bool _isAutoscrolling;
    private Windows.Foundation.Point _autoscrollOrigin;
    private double _autoscrollDeltaY;
    private ScrollViewer? _clipsScrollViewer;

    private void EnsureScrollViewerHooked()
    {
        if (_clipsScrollViewer == null && ClipsGridView != null)
        {
            _clipsScrollViewer = FindVisualChild<ScrollViewer>(ClipsGridView);
        }
    }

    private void UpdateSortButtonUI()
    {
        var loc = App.GetService<Services.ILocalizationService>();
        string label;
        string glyph;

        switch (ViewModel.SortIndex)
        {
            case 0:
                label = loc?["Sort_NewestDate"] ?? "Newest Date";
                glyph = "\uE787";
                break;
            case 1:
                label = loc?["Sort_OldestDate"] ?? "Oldest Date";
                glyph = "\uE787";
                break;
            case 2:
                label = loc?["Sort_TitleAZ"] ?? "Title (A to Z)";
                glyph = "\uE8D2";
                break;
            case 3:
                label = loc?["Sort_TitleZA"] ?? "Title (Z to A)";
                glyph = "\uE8D2";
                break;
            case 4:
                label = loc?["Sort_Duration"] ?? "Duration";
                glyph = "\uE916";
                break;
            case 5:
                label = loc?["Sort_FileSize"] ?? "File Size";
                glyph = "\uE74C";
                break;
            default:
                label = loc?["Sort_NewestDate"] ?? "Newest Date";
                glyph = "\uE787";
                break;
        }

        if (CurrentSortText != null) CurrentSortText.Text = label;
        if (CurrentSortIcon != null) CurrentSortIcon.Glyph = glyph;
    }

    private void OnSortMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int sortIndex))
        {
            ViewModel.SortIndex = sortIndex;
            UpdateSortButtonUI();
            ScrollClipsToTop();
        }
    }

    private void OnFlyoutOpening(object? sender, object e)
    {
        if (sender is MenuFlyout menuFlyout)
        {
            if (this.XamlRoot != null)
            {
                menuFlyout.XamlRoot = this.XamlRoot;
            }

            var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;

            Style? baseStyle = null;
            if (Application.Current.Resources.TryGetValue("DefaultMenuFlyoutPresenterStyle", out var ds1) && ds1 is Style s1)
            {
                baseStyle = s1;
            }
            else if (Application.Current.Resources.TryGetValue(typeof(MenuFlyoutPresenter), out var ds2) && ds2 is Style s2)
            {
                baseStyle = s2;
            }

            var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
            if (baseStyle != null)
            {
                presenterStyle.BasedOn = baseStyle;
            }
            presenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, currentTheme));
            // Library menus follow the same backdrop blur settings as the player flyouts
            var menuTint = currentTheme == ElementTheme.Light
                ? Windows.UI.Color.FromArgb(255, 249, 249, 249)
                : Windows.UI.Color.FromArgb(255, 32, 32, 32);
            presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, Helpers.Backdrop.Create(menuTint, Helpers.Backdrop.Surface.Flyout)));
            menuFlyout.MenuFlyoutPresenterStyle = presenterStyle;

            foreach (var item in menuFlyout.Items)
            {
                if (item is FrameworkElement fe)
                {
                    fe.RequestedTheme = currentTheme;
                }
            }
        }
    }

    private void OnFlyoutOpened(object? sender, object e)
    {
        if (sender is MenuFlyout menuFlyout)
        {
            var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;
            foreach (var item in menuFlyout.Items)
            {
                if (item is FrameworkElement fe)
                {
                    fe.RequestedTheme = currentTheme;
                    var parent = VisualTreeHelper.GetParent(fe);
                    while (parent != null)
                    {
                        if (parent is FrameworkElement feParent)
                        {
                            feParent.RequestedTheme = currentTheme;
                        }
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                }
            }
        }
    }

    private void UpdateViewOptionsMenuUI()
    {
        if (ViewDensityComfortableItem != null)
            ViewDensityComfortableItem.IsChecked = ViewModel.ViewDensity == ViewDensityMode.Comfortable;
        if (ViewDensityCompactItem != null)
            ViewDensityCompactItem.IsChecked = ViewModel.ViewDensity == ViewDensityMode.Compact;
        if (ViewDensityLargeItem != null)
            ViewDensityLargeItem.IsChecked = ViewModel.ViewDensity == ViewDensityMode.Large;

        if (ViewGroupingNoneItem != null)
            ViewGroupingNoneItem.IsChecked = ViewModel.DateGrouping == DateGroupingMode.None;
        if (ViewGroupingDayItem != null)
            ViewGroupingDayItem.IsChecked = ViewModel.DateGrouping == DateGroupingMode.Day;
        if (ViewGroupingMonthItem != null)
            ViewGroupingMonthItem.IsChecked = ViewModel.DateGrouping == DateGroupingMode.Month;
        if (ViewGroupingYearItem != null)
            ViewGroupingYearItem.IsChecked = ViewModel.DateGrouping == DateGroupingMode.Year;

        if (ViewMetadataHoverItem != null)
            ViewMetadataHoverItem.IsChecked = ViewModel.MetadataHoverOnly;
    }

    private void OnViewDensityItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int densityVal))
        {
            ViewModel.ViewDensity = (ViewDensityMode)densityVal;
            UpdateViewOptionsMenuUI();
            _lastCalculatedWidth = -1;
            _lastColumnCount = -1;
            UpdateGridResponsiveLayout();
        }
    }

    private void OnViewGroupingItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int groupingVal))
        {
            ViewModel.DateGrouping = (DateGroupingMode)groupingVal;
            UpdateGridItemsSource();
            UpdateViewOptionsMenuUI();
        }
    }

    private void OnViewMetadataHoverToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            ViewModel.MetadataHoverOnly = item.IsChecked;
            UpdateViewOptionsMenuUI();
            UpdateCardsMetadataVisibility();
        }
    }

    private void OnClipCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            _activeCardGrids.Add(card);
            ApplyCardIdleMetadataState(card);

            if (FindParent<ItemsWrapGrid>(card) is ItemsWrapGrid wrapGrid)
            {
                if (wrapGrid != SkeletonItemsControl?.ItemsPanelRoot)
                {
                    _activeGroupWrapGrids.Add(wrapGrid);
                    if (_currentCardWidth > 0 && Math.Abs(wrapGrid.ItemWidth - _currentCardWidth) > 0.5)
                    {
                        wrapGrid.ItemWidth = _currentCardWidth;
                        wrapGrid.ItemHeight = _currentCardHeight;
                        wrapGrid.MaximumRowsOrColumns = -1;
                    }
                }
                else if (_currentCardWidth <= 0)
                {
                    UpdateGridResponsiveLayout();
                }
            }
        }
    }

    private void OnClipCardUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            _activeCardGrids.Remove(card);
        }
    }

    private readonly Dictionary<FrameworkElement, Storyboard> _skeletonStoryboards = new();

    /// <summary>The shimmer loops forever, so pause it whenever the skeleton isn't on screen.</summary>
    private void UpdateSkeletonAnimations()
    {
        bool active = ViewModel.ShowSkeletonLoading;
        foreach (var sb in _skeletonStoryboards.Values)
        {
            if (active) sb.Resume();
            else sb.Pause();
        }
    }

    private void OnSkeletonCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && !_skeletonStoryboards.ContainsKey(elem))
        {
            if (IsReduceMotionEnabled())
            {
                // A static placeholder instead of a looping shimmer
                elem.Opacity = 0.6;
                return;
            }

            var anim = new DoubleAnimation
            {
                From = 0.35,
                To = 0.85,
                Duration = TimeSpan.FromMilliseconds(1100),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, elem);
            Storyboard.SetTargetProperty(anim, "Opacity");
            _skeletonStoryboards[elem] = sb;
            sb.Begin();
            if (!ViewModel.ShowSkeletonLoading)
            {
                sb.Pause();
            }
        }
    }

    private void UpdateCardsMetadataVisibility()
    {
        foreach (var card in _activeCardGrids)
        {
            ApplyCardIdleMetadataState(card);
        }
    }

    private void ApplyCardIdleMetadataState(FrameworkElement card)
    {
        bool hoverOnly = ViewModel.MetadataHoverOnly;
        if (card.FindName("TitleTransform") is TranslateTransform tt)
        {
            tt.Y = hoverOnly ? 18.0 : 0.0;
        }
        if (card.FindName("MetadataTransform") is TranslateTransform mt)
        {
            mt.Y = hoverOnly ? 18.0 : 0.0;
        }
        if (card.FindName("MetadataRow") is UIElement mr)
        {
            mr.Opacity = hoverOnly ? 0.0 : 0.85;
        }
    }

    private void UpdateGridItemsSource()
    {
        if (ClipsGridView == null) return;
        if (ViewModel.DateGrouping == DateGroupingMode.None)
        {
            ClipsGridView.ItemsSource = ViewModel.FilteredClips;
        }
        else
        {
            if (ClipsGroupedSource != null)
            {
                ClipsGroupedSource.Source = ViewModel.GroupedClips;
                ClipsGridView.ItemsSource = ClipsGroupedSource.View;
            }
        }
        _lastCalculatedWidth = -1;
        _lastColumnCount = -1;
        UpdateGridResponsiveLayout();
    }

    private void OnClipsGridViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridResponsiveLayout(forcedGridWidth: e.NewSize.Width);
    }

    private void FindAndRegisterGroupWrapGrids(DependencyObject? parent)
    {
        if (parent == null) return;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ItemsWrapGrid wg && wg != SkeletonItemsControl?.ItemsPanelRoot)
            {
                _activeGroupWrapGrids.Add(wg);
            }
            else
            {
                FindAndRegisterGroupWrapGrids(child);
            }
        }
    }

    private double GetClipsAvailableWidth(double forcedGridWidth = -1)
    {
        if (ClipsGridView == null) return 0;
        double containerWidth = 0;
        if (forcedGridWidth > 0)
        {
            containerWidth = forcedGridWidth - 16.0;
        }
        else
        {
            _clipsScrollViewer ??= FindVisualChild<ScrollViewer>(ClipsGridView);
            if (_clipsScrollViewer != null && _clipsScrollViewer.ViewportWidth > 0)
            {
                containerWidth = _clipsScrollViewer.ViewportWidth;
                // If the scroll viewer hasn't shown the vertical scrollbar yet, but there will be scrolling items,
                // reserve the standard 16 DIP scrollbar width so scrollbar appearance won't trigger premature wrapping!
                if (_clipsScrollViewer.ComputedVerticalScrollBarVisibility != Visibility.Visible &&
                    (ViewModel.FilteredClips.Count > 6 || ViewModel.GroupedClips.Count > 1))
                {
                    containerWidth -= 16.0;
                }
            }
            else if (ClipsGridView.ActualWidth > 0)
            {
                // Fallback when ViewportWidth hasn't measured yet: subtract standard vertical scrollbar (16 DIPs)
                containerWidth = ClipsGridView.ActualWidth - 16.0;
            }
            else if (ActualWidth > 0)
            {
                // First launch fallback before ClipsGridView layout pass:
                double sbWidth = ViewModel.IsSidebarOpen ? (_sidebarWidth > 0 ? _sidebarWidth : 316.0) : 0.0;
                containerWidth = ActualWidth - sbWidth - 16.0;
            }

            if (containerWidth <= 0 && ClipsGridView.ActualWidth > 0)
            {
                containerWidth = ClipsGridView.ActualWidth;
            }
        }

        containerWidth -= (ClipsGridView.Padding.Left + ClipsGridView.Padding.Right);
        return Math.Max(0, containerWidth);
    }

    private (double targetWidth, double minWidth, double maxWidth) GetDensityBounds()
    {
        return ViewModel.ViewDensity switch
        {
            ViewDensityMode.Compact => (210.0, 168.0, 260.0),
            ViewDensityMode.Large => (360.0, 288.0, 450.0),
            _ => (280.0, 224.0, 350.0)
        };
    }

    private int CalculateResponsiveColumns(double availableWidth, int currentColumns)
    {
        if (availableWidth <= 50) return 1;
        var (targetWidth, minWidth, maxWidth) = GetDensityBounds();

        int columns = currentColumns > 0
            ? currentColumns
            : (int)Math.Max(1, Math.Round(availableWidth / targetWidth, MidpointRounding.AwayFromZero));

        // 1. Shrink Priority: Only wrap down when cards reach the minimum shrink cap
        while (columns > 1 && ((availableWidth - 2.0) / columns) < minWidth)
        {
            columns--;
        }

        // 2. Expand: Only expand when adding a column still leaves each card at or above targetWidth,
        // or when current cards have stretched past the maximum stretch cap
        while ((((availableWidth - 2.0) / (columns + 1)) >= targetWidth || ((availableWidth - 2.0) / columns) > maxWidth)
               && ((availableWidth - 2.0) / (columns + 1)) >= minWidth)
        {
            columns++;
        }

        return Math.Max(1, columns);
    }

    private void UpdateGridResponsiveLayout(double forcedGridWidth = -1)
    {
        if (ClipsGridView == null) return;

        EnsureScrollViewerHooked();
        bool wasAtTop = _clipsScrollViewer == null || _clipsScrollViewer.VerticalOffset <= 2.0;

        double availableWidth = GetClipsAvailableWidth(forcedGridWidth);
        if (availableWidth <= 50) return;

        int columns = CalculateResponsiveColumns(availableWidth, _lastColumnCount);
        // Subtract 2.0 DIPs before division to ensure (columns * calculatedWidth) is strictly <= availableWidth
        // so subpixel rounding in DirectComposition / layout engine NEVER causes early wrap!
        double calculatedWidth = Math.Floor((availableWidth - 2.0) / columns);
        double calculatedHeight = Math.Round(calculatedWidth * 9.0 / 16.0);

        // Apply in the same layout pass as the size change. Setting ItemWidth only re-arranges the
        // realized cards, and deferring it (the old 16ms throttle) is what made cards trail the window edge.
        if (columns == _lastColumnCount && Math.Abs(calculatedWidth - _lastCalculatedWidth) <= 0.5)
        {
            return;
        }

        _lastColumnCount = columns;
        _lastCalculatedWidth = calculatedWidth;
        _currentCardWidth = calculatedWidth;
        _currentCardHeight = calculatedHeight;

        if (ClipsGridView.ItemsPanelRoot is ItemsWrapGrid rootWrapGrid && rootWrapGrid != SkeletonItemsControl?.ItemsPanelRoot)
        {
            _activeGroupWrapGrids.Add(rootWrapGrid);
        }

        if (_activeGroupWrapGrids.Count == 0)
        {
            FindAndRegisterGroupWrapGrids(ClipsGridView);
        }

        bool appliedToAny = false;
        if (ViewModel.DateGrouping == DateGroupingMode.None)
        {
            if (ClipsGridView.ItemsPanelRoot is ItemsWrapGrid flatWrapGrid)
            {
                flatWrapGrid.ItemWidth = calculatedWidth;
                flatWrapGrid.ItemHeight = calculatedHeight;
                flatWrapGrid.MaximumRowsOrColumns = columns;
                appliedToAny = true;
            }
        }

        if (SkeletonItemsControl?.ItemsPanelRoot is ItemsWrapGrid skeletonWrapGrid)
        {
            skeletonWrapGrid.ItemWidth = calculatedWidth;
            skeletonWrapGrid.ItemHeight = calculatedHeight;
            skeletonWrapGrid.MaximumRowsOrColumns = columns;
        }

        if (_activeGroupWrapGrids.Count > 0)
        {
            UpdateGroupWrapGrids(calculatedWidth, calculatedHeight, columns);
            appliedToAny = true;
        }

        if (wasAtTop && _clipsScrollViewer != null)
        {
            _clipsScrollViewer.ChangeView(null, 0.0, null, disableAnimation: true);
        }

        if (!appliedToAny)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _lastCalculatedWidth = -1;
                _lastColumnCount = -1;
                UpdateGridResponsiveLayout();
            });
        }
        else if (wasAtTop)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_clipsScrollViewer != null && _clipsScrollViewer.VerticalOffset > 0.0 && _clipsScrollViewer.VerticalOffset <= 32.0)
                {
                    _clipsScrollViewer.ChangeView(null, 0.0, null, disableAnimation: true);
                }
            });
        }
    }

    private void UpdateGroupWrapGrids(double width, double height, int columns = -1)
    {
        _activeGroupWrapGrids.RemoveWhere(wg => wg.XamlRoot == null);
        foreach (var wrapGrid in _activeGroupWrapGrids)
        {
            wrapGrid.ItemWidth = width;
            wrapGrid.ItemHeight = height;
            if (columns > 0)
            {
                wrapGrid.MaximumRowsOrColumns = columns;
            }
        }
    }

    private void OnClipContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is GridViewItem gvi && !args.InRecycleQueue)
        {
            if (gvi.RequestedTheme != ActualTheme)
            {
                gvi.RequestedTheme = ActualTheme;
            }

            if (FindParent<ItemsWrapGrid>(gvi) is ItemsWrapGrid wrapGrid)
            {
                if (wrapGrid != SkeletonItemsControl?.ItemsPanelRoot)
                {
                    _activeGroupWrapGrids.Add(wrapGrid);
                    if (_currentCardWidth > 0 && Math.Abs(wrapGrid.ItemWidth - _currentCardWidth) > 0.5)
                    {
                        wrapGrid.ItemWidth = _currentCardWidth;
                        wrapGrid.ItemHeight = _currentCardHeight;
                        wrapGrid.MaximumRowsOrColumns = -1;
                    }
                }
            }
        }

        if (args.InRecycleQueue)
        {
            // Release the bitmap once the card scrolls out of view. The thumbnail service keeps a
            // bounded LRU (plus the disk cache), so scrolling back is still instant, but memory no
            // longer grows with every clip that has ever been on screen.
            if (args.Item is Models.GameClip recycledClip)
            {
                // After a Reset the same clip can be realized in a new container before its old
                // container's recycle notification arrives. Releasing right away would blank the
                // visible card, so release only once no container shows the clip anymore.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ClipsGridView?.ContainerFromItem(recycledClip) == null)
                    {
                        recycledClip.Thumbnail = null;
                    }
                });
            }
            return;
        }

        if (args.Item is Models.GameClip clip && clip.Thumbnail == null)
        {
            LoadClipThumbnail(clip);
        }
    }

    private void LoadClipThumbnail(Models.GameClip clip)
    {
        if (clip.Thumbnail != null) return;
        _thumbnailService ??= App.GetService<Services.IThumbnailService>();
        if (_thumbnailService == null) return;

        if (_thumbnailService.TryGetFromMemoryCache(clip.FilePath, out var cachedThumb))
        {
            clip.Thumbnail = cachedThumb;
            return;
        }

        _ = Task.Run(async () =>
        {
            var thumb = await _thumbnailService.GetThumbnailAsync(clip.FilePath);
            if (thumb != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (clip.Thumbnail == null)
                    {
                        clip.Thumbnail = thumb;
                    }
                });
            }
        });
    }

    private void OnClipCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (this.ProtectedCursor != null)
        {
            this.ProtectedCursor = null;
        }
        if (sender is FrameworkElement card)
        {
            _hoveredClip = card.DataContext as GameClip;
            AnimateCardHover(card, isHovered: true);
        }
    }

    private void OnClipCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            if (ReferenceEquals(_hoveredClip, card.DataContext))
            {
                _hoveredClip = null;
            }
            AnimateCardHover(card, isHovered: false);
        }
    }

    private void AnimateCardHover(FrameworkElement card, bool isHovered)
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var activeEase = isHovered ? (EasingFunctionBase)easeOut : easeInOut;
        var duration = Helpers.Motion.Duration(isHovered ? 260 : 220);
        // Reduced motion keeps the outline and overlay feedback but drops the lift
        double hoverScale = IsReduceMotionEnabled() ? 1.0 : 1.02;

        // 1. Elevate Z-Index so hovered card rests above neighbor cards
        if (FindParent<GridViewItem>(card) is GridViewItem hoveredGvi)
        {
            Canvas.SetZIndex(hoveredGvi, isHovered ? 10 : 0);
        }

        // Capture current in-flight property values before stopping previous storyboard
        var titleTransform = card.FindName("TitleTransform") as TranslateTransform;
        double currentTitleY = titleTransform?.Y ?? (isHovered ? 18.0 : 0.0);

        var metaTransform = card.FindName("MetadataTransform") as TranslateTransform;
        double currentMetaY = metaTransform?.Y ?? (isHovered ? 18.0 : 0.0);

        var metadataRow = card.FindName("MetadataRow") as UIElement;
        double currentMetaOpacity = metadataRow?.Opacity ?? (isHovered ? 0.0 : 0.85);

        var highlightBorder = card.FindName("HoverHighlightBorder") as UIElement;
        double currentBorderOpacity = highlightBorder?.Opacity ?? (isHovered ? 0.0 : 1.0);

        var scale = card.FindName("CardScale") as ScaleTransform;
        double currentScaleX = scale?.ScaleX ?? (isHovered ? 1.0 : 1.02);
        double currentScaleY = scale?.ScaleY ?? (isHovered ? 1.0 : 1.02);

        var playOverlay = card.FindName("PlayOverlay") as UIElement;
        double currentPlayOpacity = playOverlay?.Opacity ?? (isHovered ? 0.0 : 1.0);

        var playOverlayScale = card.FindName("PlayOverlayScale") as ScaleTransform;
        double currentPlayScaleX = playOverlayScale?.ScaleX ?? (isHovered ? 0.8 : 1.0);
        double currentPlayScaleY = playOverlayScale?.ScaleY ?? (isHovered ? 0.8 : 1.0);

        if (card.Tag is Storyboard oldSb)
        {
            oldSb.Stop();
            card.Tag = null;
        }

        var sb = new Storyboard();

        // 2. Accent Border
        if (highlightBorder != null)
        {
            var animBorder = new DoubleAnimation
            {
                From = currentBorderOpacity,
                To = isHovered ? 1.0 : 0.0,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animBorder);
            Storyboard.SetTarget(animBorder, highlightBorder);
            Storyboard.SetTargetProperty(animBorder, "Opacity");
        }

        // 3. Card Gentle Scale from Center
        if (scale != null)
        {
            var animScaleX = new DoubleAnimation
            {
                From = currentScaleX,
                To = isHovered ? hoverScale : 1.0,
                Duration = duration,
                EasingFunction = activeEase
            };
            var animScaleY = new DoubleAnimation
            {
                From = currentScaleY,
                To = isHovered ? hoverScale : 1.0,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animScaleX);
            sb.Children.Add(animScaleY);
            Storyboard.SetTarget(animScaleX, scale);
            Storyboard.SetTargetProperty(animScaleX, "ScaleX");
            Storyboard.SetTarget(animScaleY, scale);
            Storyboard.SetTargetProperty(animScaleY, "ScaleY");
        }

        // 4. Play Button Overlay Animation (Compositor-friendly fade & scale)
        if (playOverlay != null)
        {
            var animOpacity = new DoubleAnimation
            {
                From = currentPlayOpacity,
                To = isHovered ? 1.0 : 0.0,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animOpacity);
            Storyboard.SetTarget(animOpacity, playOverlay);
            Storyboard.SetTargetProperty(animOpacity, "Opacity");

            if (playOverlayScale != null)
            {
                var animScaleX = new DoubleAnimation
                {
                    From = currentPlayScaleX,
                    To = isHovered ? 1.0 : 0.8,
                    Duration = duration,
                    EasingFunction = activeEase
                };
                var animScaleY = new DoubleAnimation
                {
                    From = currentPlayScaleY,
                    To = isHovered ? 1.0 : 0.8,
                    Duration = duration,
                    EasingFunction = activeEase
                };
                sb.Children.Add(animScaleX);
                sb.Children.Add(animScaleY);
                Storyboard.SetTarget(animScaleX, playOverlayScale);
                Storyboard.SetTargetProperty(animScaleX, "ScaleX");
                Storyboard.SetTarget(animScaleY, playOverlayScale);
                Storyboard.SetTargetProperty(animScaleY, "ScaleY");
            }
        }

        // 5. Title & Metadata Slide Animation
        bool hoverOnly = ViewModel.MetadataHoverOnly;
        if (titleTransform != null)
        {
            double targetTitleY = (hoverOnly && !isHovered) ? 18.0 : 0.0;
            var animTitleY = new DoubleAnimation
            {
                From = currentTitleY,
                To = targetTitleY,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animTitleY);
            Storyboard.SetTarget(animTitleY, titleTransform);
            Storyboard.SetTargetProperty(animTitleY, "Y");
        }

        if (metaTransform != null)
        {
            double targetMetaY = (hoverOnly && !isHovered) ? 18.0 : 0.0;
            var animMetaY = new DoubleAnimation
            {
                From = currentMetaY,
                To = targetMetaY,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animMetaY);
            Storyboard.SetTarget(animMetaY, metaTransform);
            Storyboard.SetTargetProperty(animMetaY, "Y");
        }

        if (metadataRow != null)
        {
            double targetOpacity = isHovered ? 1.0 : (hoverOnly ? 0.0 : 0.85);
            var animMetaOpacity = new DoubleAnimation
            {
                From = currentMetaOpacity,
                To = targetOpacity,
                Duration = duration,
                EasingFunction = activeEase
            };
            sb.Children.Add(animMetaOpacity);
            Storyboard.SetTarget(animMetaOpacity, metadataRow);
            Storyboard.SetTargetProperty(animMetaOpacity, "Opacity");
        }

        if (sb.Children.Count > 0)
        {
            card.Tag = sb;
            sb.Completed += (s, e) =>
            {
                if (ReferenceEquals(card.Tag, sb))
                {
                    card.Tag = null;
                }
            };
            Helpers.Motion.Begin(sb);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyThemeVisuals(ActualTheme);
        if (ViewModel.Directories.Count == 0 && ViewModel.AllClips.Count == 0)
        {
            await ViewModel.SyncDirectoriesAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopAutoscroll();
    }

    private void OnClipsGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoscrolling)
            {
                StopAutoscroll();
            }
            else
            {
                StartAutoscroll(pt.Position);
            }
            e.Handled = true;
        }
        else if (_isAutoscrolling)
        {
            StopAutoscroll();
            e.Handled = true;
        }
    }

    private void StartAutoscroll(Windows.Foundation.Point origin)
    {
        _clipsScrollViewer ??= FindVisualChild<ScrollViewer>(ClipsGridView);
        if (_clipsScrollViewer == null) return;

        _isAutoscrolling = true;
        _autoscrollOrigin = origin;
        _autoscrollDeltaY = 0;

        if (AutoscrollCanvas != null && AutoscrollIndicator != null)
        {
            Canvas.SetLeft(AutoscrollIndicator, origin.X);
            Canvas.SetTop(AutoscrollIndicator, origin.Y);
            AutoscrollCanvas.Visibility = Visibility.Visible;
        }

        CompositionTarget.Rendering += OnAutoscrollRendering;
    }

    private void StopAutoscroll()
    {
        if (!_isAutoscrolling) return;
        _isAutoscrolling = false;
        _autoscrollDeltaY = 0;

        if (AutoscrollCanvas != null)
        {
            AutoscrollCanvas.Visibility = Visibility.Collapsed;
        }

        CompositionTarget.Rendering -= OnAutoscrollRendering;
    }

    private void OnClipsGridPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isAutoscrolling)
        {
            var current = e.GetCurrentPoint(this).Position;
            _autoscrollDeltaY = current.Y - _autoscrollOrigin.Y;
        }
    }

    private void OnClipsGridPointerReleased(object sender, PointerRoutedEventArgs e)
    {
    }

    private void OnClipsGridPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_isAutoscrolling)
        {
            StopAutoscroll();
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isAutoscrolling && e.Key == Windows.System.VirtualKey.Escape)
        {
            StopAutoscroll();
            e.Handled = true;
            return;
        }

        // Escape only matters while typing in search (it already has focus, so KeyDown reaches us);
        // the search box swallows Escape itself, hence handledEventsToo on this handler.
        bool isTyping = e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox;
        if (e.Key == Windows.System.VirtualKey.Escape && isTyping && !string.IsNullOrEmpty(ViewModel.SearchQuery))
        {
            ViewModel.SearchQuery = string.Empty;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Library shortcuts are keyboard accelerators rather than KeyDown handlers: the card grid can't take
    /// focus, so KeyDown never reached the page unless the search box happened to be focused.
    /// Accelerators fire whenever the page is in the live tree, so each one checks it actually owns input.
    /// </summary>
    private void RegisterLibraryShortcuts()
    {
        void Add(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Func<bool> action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (s, args) =>
            {
                if (!CanHandleLibraryShortcut()) return;
                args.Handled = action();
            };
            KeyboardAccelerators.Add(accelerator);
        }

        Add(Windows.System.VirtualKey.F, Windows.System.VirtualKeyModifiers.Control, () =>
        {
            ClipsSearchBox?.Focus(FocusState.Keyboard);
            return true;
        });

        Add(Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None, () =>
        {
            _ = ViewModel.RefreshClipsAsync();
            return true;
        });

        Add(Windows.System.VirtualKey.Delete, Windows.System.VirtualKeyModifiers.None, () =>
        {
            var clip = GetShortcutTargetClip();
            if (clip == null || IsTextInputFocused()) return false;
            _ = ShowDeleteConfirmDialogAsync(clip);
            return true;
        });

        Add(Windows.System.VirtualKey.F2, Windows.System.VirtualKeyModifiers.None, () =>
        {
            var clip = GetShortcutTargetClip();
            if (clip == null || IsTextInputFocused()) return false;
            _ = ShowLocalRenameDialogAsync(clip);
            return true;
        });

        // Accelerator tooltips would otherwise be appended to the page's tooltips
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private bool CanHandleLibraryShortcut()
    {
        if (XamlRoot == null) return false;
        if ((App.Window as MainWindow)?.IsPlayerVisible == true) return false;
        if (App.Window is MainWindow mw && !ReferenceEquals(mw.NavigationFrame.Content, this)) return false;
        // Don't act behind an open dialog or menu
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count == 0;
    }

    /// <summary>
    /// True while the user is editing text, where Delete/F2 belong to the text box. An empty search box
    /// that merely kept focus (e.g. after Esc cleared it) shouldn't block the card shortcuts.
    /// </summary>
    private bool IsTextInputFocused() =>
        XamlRoot != null && FocusManager.GetFocusedElement(XamlRoot) switch
        {
            TextBox textBox => !string.IsNullOrEmpty(textBox.Text),
            PasswordBox => true,
            _ => false
        };

    /// <summary>The card under the pointer, falling back to the last right-clicked card.</summary>
    private GameClip? GetShortcutTargetClip() => _hoveredClip ?? ViewModel.SelectedClip;

    private void OnAutoscrollRendering(object? sender, object e)
    {
        if (!_isAutoscrolling || _clipsScrollViewer == null) return;

        const double DeadZone = 12.0;
        if (Math.Abs(_autoscrollDeltaY) <= DeadZone) return;

        double effectiveDelta = _autoscrollDeltaY > 0 ? _autoscrollDeltaY - DeadZone : _autoscrollDeltaY + DeadZone;
        double speed = Math.Sign(effectiveDelta) * Math.Min(50.0, Math.Pow(Math.Abs(effectiveDelta) / 8.0, 1.35));
        double targetOffset = _clipsScrollViewer.VerticalOffset + speed;
        _clipsScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
    }

    private void ScrollClipsToTop()
    {
        if (ClipsGridView == null) return;
        EnsureScrollViewerHooked();
        _clipsScrollViewer?.ChangeView(null, 0.0, null, disableAnimation: true);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ClipsGridView == null) return;
            EnsureScrollViewerHooked();
            _clipsScrollViewer?.ChangeView(null, 0.0, null, disableAnimation: true);
        });
    }

    private async void OnHomeTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectHomeAsync();
        ScrollClipsToTop();
        UpdateActiveIndicator(animate: true);
    }

    private async void OnFavoritesTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectFavoritesAsync();
        ScrollClipsToTop();
        UpdateActiveIndicator(animate: true);
    }

    private async void OnSavedMomentsTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectSavedMomentsAsync();
        ScrollClipsToTop();
        UpdateActiveIndicator(animate: true);
    }


    // ---------------------------------------------------------------------------------------------
    // Folder tree expand / collapse
    //
    // The tree itself expands and collapses immediately (like File Explorer's navigation pane); only the
    // child rows animate, on the compositor: a short staggered fade + slide in, and a quick fade out before
    // a branch closes. Nothing touches the rows' XAML Height/Opacity, so an interrupted animation can never
    // leave a folder invisible, and a recycled row is reset before it is shown again.
    // ---------------------------------------------------------------------------------------------

    private readonly System.Collections.Generic.Dictionary<DirectoryNode, long> _expandingNodes = new();
    private readonly System.Collections.Generic.HashSet<DirectoryNode> _collapsingNodes = new();
    private readonly System.Collections.Generic.Dictionary<DirectoryNode, Microsoft.UI.Composition.CompositionScopedBatch> _collapseBatches = new();
    private Microsoft.UI.Composition.CompositionEasingFunction? _folderEaseOut;
    private Microsoft.UI.Composition.CompositionEasingFunction? _folderEaseIn;

    /// <summary>How long after an expand newly realized rows still play the entrance animation.</summary>
    private const int ExpandWindowMs = 400;

    private static void CollectVisibleDescendants(DirectoryNode parent, System.Collections.Generic.List<DirectoryNode> list)
    {
        foreach (var child in parent.SubDirectories)
        {
            list.Add(child);
            if (child.IsExpanded && child.SubDirectories.Count > 0)
            {
                CollectVisibleDescendants(child, list);
            }
        }
    }

    private bool IsInsideExpandingBranch(DirectoryNode node)
    {
        long now = Environment.TickCount64;
        foreach (var kv in _expandingNodes)
        {
            if (now <= kv.Value && node != kv.Key &&
                node.FullPath.StartsWith(kv.Key.FullPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Position of each row in the branch being opened, for the stagger.</summary>
    private readonly System.Collections.Generic.Dictionary<DirectoryNode, int> _expandOrder = new();

    private void EnsureFolderEasings(Microsoft.UI.Composition.Compositor compositor)
    {
        // Windows 11 motion curves: fast-out for entrances, accelerate for exits
        _folderEaseOut ??= compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));
        _folderEaseIn ??= compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.7f, 0f), new System.Numerics.Vector2(1f, 0.5f));
    }

    private static void ResetFolderRow(UIElement row)
    {
        // Translation must be enabled before it can be stopped or set
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(row);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
    }

    private void AnimateFolderRowIn(UIElement row, int index)
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(row);
        var compositor = visual.Compositor;
        EnsureFolderEasings(compositor);

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 8) * 18);
        var duration = TimeSpan.FromMilliseconds(220);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, _folderEaseOut);
        fade.Duration = duration;
        fade.DelayTime = delay;
        fade.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, -8f, 0f));
        slide.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, _folderEaseOut);
        slide.Duration = duration;
        slide.DelayTime = delay;
        slide.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
    }

    private void AnimateFolderRowOut(UIElement row, int reverseIndex)
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(row);
        var compositor = visual.Compositor;
        EnsureFolderEasings(compositor);

        var delay = TimeSpan.FromMilliseconds(Math.Min(reverseIndex, 6) * 10);
        var duration = TimeSpan.FromMilliseconds(110);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, _folderEaseIn);
        fade.Duration = duration;
        fade.DelayTime = delay;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, -6f, 0f), _folderEaseIn);
        slide.Duration = duration;
        slide.DelayTime = delay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>Animates the rows of a branch that just opened (rows realized later animate from OnTreeViewItemLoading).</summary>
    private void BeginExpandAnimation(DirectoryNode dirNode)
    {
        if (IsReduceMotionEnabled()) return;
        _expandingNodes[dirNode] = Environment.TickCount64 + ExpandWindowMs;
        var branch = new System.Collections.Generic.List<DirectoryNode>();
        CollectVisibleDescendants(dirNode, branch);
        _expandOrder.Clear();
        for (int i = 0; i < branch.Count; i++) _expandOrder[branch[i]] = i;

        // Rows that already exist (re-expanding a cached branch) don't raise Loading again, so animate them
        // once the tree has laid the branch out
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (DirectoriesTreeView == null || !dirNode.IsExpanded) return;
            var rows = new System.Collections.Generic.List<DirectoryNode>();
            CollectVisibleDescendants(dirNode, rows);
            for (int i = 0; i < rows.Count; i++)
            {
                if (DirectoriesTreeView.ContainerFromItem(rows[i]) is TreeViewItem container && container.IsLoaded)
                {
                    AnimateFolderRowIn(container, i);
                }
            }
            UpdateActiveIndicator(animate: true);
        });
    }

    private void CancelCollapse(DirectoryNode dirNode)
    {
        if (!_collapsingNodes.Remove(dirNode)) return;
        _collapseBatches.Remove(dirNode);
        if (DirectoriesTreeView == null) return;
        var rows = new System.Collections.Generic.List<DirectoryNode>();
        CollectVisibleDescendants(dirNode, rows);
        foreach (var row in rows)
        {
            if (DirectoriesTreeView.ContainerFromItem(row) is TreeViewItem container)
            {
                ResetFolderRow(container);
            }
        }
    }

    private void OnDirectoryExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode == null) return;

        CancelCollapse(dirNode);
        dirNode.IsExpanded = true;
        if (sender.ContainerFromItem(dirNode) is TreeViewItem parentTvi)
        {
            VisualStateManager.GoToState(parentTvi, "Expanded", !IsReduceMotionEnabled());
        }

        if (!_expandingNodes.TryGetValue(dirNode, out long until) || Environment.TickCount64 > until)
        {
            BeginExpandAnimation(dirNode);
        }
        if (IsReduceMotionEnabled())
        {
            UpdateActiveIndicator(animate: false);
        }
    }

    private void OnDirectoryCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode != null)
        {
            dirNode.IsExpanded = false;
            _expandingNodes.Remove(dirNode);
            _collapsingNodes.Remove(dirNode);
            _collapseBatches.Remove(dirNode);
            if (sender.ContainerFromItem(dirNode) is TreeViewItem parentTvi)
            {
                VisualStateManager.GoToState(parentTvi, "Collapsed", !IsReduceMotionEnabled());
            }
        }
        UpdateActiveIndicator(animate: !IsReduceMotionEnabled());
    }

    private void ExpandDirectoryWithAnimation(DirectoryNode node)
    {
        CancelCollapse(node);
        BeginExpandAnimation(node);
        node.IsExpanded = true;
        if (DirectoriesTreeView?.ContainerFromItem(node) is TreeViewItem parentTvi)
        {
            VisualStateManager.GoToState(parentTvi, "Expanded", !IsReduceMotionEnabled());
        }
        if (IsReduceMotionEnabled())
        {
            UpdateActiveIndicator(animate: false);
        }
    }

    private void OnChevronPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement el && el.DataContext is DirectoryNode node && node.HasSubDirectories)
        {
            bool isExpanded = node.IsExpanded && !_collapsingNodes.Contains(node);
            if (isExpanded)
            {
                _ = CollapseDirectoryWithAnimationAsync(node);
            }
            else
            {
                ExpandDirectoryWithAnimation(node);
            }
        }
    }

    private System.Threading.Tasks.Task CollapseDirectoryWithAnimationAsync(DirectoryNode dirNode)
    {
        if (_collapsingNodes.Contains(dirNode)) return System.Threading.Tasks.Task.CompletedTask;
        _expandingNodes.Remove(dirNode);

        var rows = new System.Collections.Generic.List<DirectoryNode>();
        CollectVisibleDescendants(dirNode, rows);

        // Chevron turns right away; the selection pill hides if it sits inside the closing branch
        if (DirectoriesTreeView?.ContainerFromItem(dirNode) is TreeViewItem parentTvi)
        {
            VisualStateManager.GoToState(parentTvi, "Collapsed", !IsReduceMotionEnabled());
        }
        if (ViewModel.SelectedDirectory != null && rows.Contains(ViewModel.SelectedDirectory))
        {
            AnimateIndicatorVisibility(false, animate: !IsReduceMotionEnabled());
        }

        var containers = new System.Collections.Generic.List<TreeViewItem>();
        if (DirectoriesTreeView != null && !IsReduceMotionEnabled())
        {
            foreach (var row in rows)
            {
                if (DirectoriesTreeView.ContainerFromItem(row) is TreeViewItem container && container.IsLoaded)
                {
                    containers.Add(container);
                }
            }
        }

        if (containers.Count == 0)
        {
            dirNode.IsExpanded = false;
            UpdateActiveIndicator(animate: !IsReduceMotionEnabled());
            return System.Threading.Tasks.Task.CompletedTask;
        }

        _collapsingNodes.Add(dirNode);
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(DirectoriesTreeView).Compositor;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        for (int i = 0; i < containers.Count; i++)
        {
            AnimateFolderRowOut(containers[i], containers.Count - 1 - i);
        }
        batch.End();
        _collapseBatches[dirNode] = batch;

        batch.Completed += (s, e) =>
        {
            // Re-expanded (or collapsed elsewhere) while fading out: that path already restored the rows
            if (!_collapseBatches.TryGetValue(dirNode, out var current) || current != batch) return;
            _collapseBatches.Remove(dirNode);
            _collapsingNodes.Remove(dirNode);

            dirNode.IsExpanded = false;
            // The rows leave the tree now; reset them so their containers come back fully visible
            foreach (var container in containers)
            {
                ResetFolderRow(container);
            }
            UpdateActiveIndicator(animate: true);
        };
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void OnFolderItemGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement grid)
        {
            grid.Opacity = 1.0;
            if (grid.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
            {
                tt.Y = 0.0;
            }
        }
    }

    private void OnFolderRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not TreeViewItem row) return;
        var node = (row.DataContext as DirectoryNode) ?? (row.Content as DirectoryNode);
        if (node == null) return;
        e.Handled = true;

        var loc = App.GetService<Services.ILocalizationService>();
        var flyout = new MenuFlyout();
        if (XamlRoot != null) flyout.XamlRoot = XamlRoot;
        flyout.Opening += OnFlyoutOpening;
        flyout.Opened += OnFlyoutOpened;

        MenuFlyoutItem Item(string key, string glyph, Action onClick, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            item.Click += (s, args) => onClick();
            flyout.Items.Add(item);
            return item;
        }

        Item("FolderMenu_Open", "\uE8E5", () => SelectFolderFromMenu(node));
        if (node.HasSubDirectories)
        {
            bool expanded = node.IsExpanded && !_collapsingNodes.Contains(node);
            Item(expanded ? "FolderMenu_Collapse" : "FolderMenu_Expand", expanded ? "\uE70E" : "\uE70D", () =>
            {
                if (expanded) _ = CollapseDirectoryWithAnimationAsync(node);
                else ExpandDirectoryWithAnimation(node);
            });
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        Item("FolderMenu_RevealExplorer", "\uEC50", () => ViewModel.OpenFolderInExplorer(node));
        Item("FolderMenu_CopyPath", "\uE8C8", () => ViewModel.CopyFolderPath(node));

        // Watch folders can be reordered; subfolders follow the file system order
        int index = ViewModel.Directories.IndexOf(node);
        if (node.IsWatchRoot && index >= 0 && ViewModel.Directories.Count > 1)
        {
            int last = ViewModel.Directories.Count - 1;
            flyout.Items.Add(new MenuFlyoutSeparator());
            var moveItem = new MenuFlyoutSubItem { Text = loc["FolderMenu_Move"], Icon = new FontIcon { Glyph = "\uE8CB" } };
            void MoveItem(string key, string glyph, int target, bool enabled)
            {
                var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
                item.Click += (s, args) => MoveWatchFolder(node, target);
                moveItem.Items.Add(item);
            }
            MoveItem("FolderMenu_MoveUp", "\uE74A", index - 1, index > 0);
            MoveItem("FolderMenu_MoveDown", "\uE74B", index + 1, index < last);
            moveItem.Items.Add(new MenuFlyoutSeparator());
            MoveItem("FolderMenu_MoveTop", "\uEDDB", 0, index > 0);
            MoveItem("FolderMenu_MoveBottom", "\uEDDC", last, index < last);
            flyout.Items.Add(moveItem);
        }

        if (node.IsWatchRoot)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            Item("FolderMenu_Remove", "\uE74D", () => _ = ViewModel.RemoveDirectoryAsync(node));
        }

        flyout.ShowAt(row, e.GetPosition(row));
    }

    private async void SelectFolderFromMenu(DirectoryNode node)
    {
        if (DirectoriesTreeView != null) DirectoriesTreeView.SelectedItem = node;
        await ViewModel.SelectDirectoryAsync(node);
        ScrollClipsToTop();
        UpdateActiveIndicator(animate: true);
    }

    private async void MoveWatchFolder(DirectoryNode node, int targetIndex)
    {
        await ViewModel.MoveWatchRootAsync(node, targetIndex);

        // The tree re-creates the moved row: restore the selection and give the row a short entrance so
        // the eye can follow where it went
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (DirectoriesTreeView == null) return;
            if (ViewModel.SelectedDirectory != null && ViewModel.CurrentSection == NavigationSection.Folder)
            {
                DirectoriesTreeView.SelectedItem = ViewModel.SelectedDirectory;
            }
            if (!IsReduceMotionEnabled() && DirectoriesTreeView.ContainerFromItem(node) is TreeViewItem moved)
            {
                AnimateFolderRowIn(moved, 0);
            }
            UpdateActiveIndicator(animate: !IsReduceMotionEnabled());
        });
    }

    private void OnFolderRowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            AnimateElementClickPulse(el, 0.98);
        }
    }

    private void OnFolderRowPointerReset(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            ResetElementScale(el);
        }
    }

    private void OnItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            AnimateElementClickPulse(el, 0.97);
        }
    }

    private void OnItemPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            ResetElementScale(el);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Update indicator & prompt
    // ---------------------------------------------------------------------------------------------

    private enum UpdateTipKind { Available, Ready, Updated, Failed }

    private UpdateTipKind _updateTipKind;
    private bool _isUpdateIndicatorShown;
    private Storyboard? _updateIndicatorStoryboard;

    private static ViewModels.SettingsViewModel SettingsVM => App.GetService<ViewModels.SettingsViewModel>();

    private void WireUpdateNotifications()
    {
        var vm = SettingsVM;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ViewModels.SettingsViewModel.IsUpdateAvailable)
                or nameof(ViewModels.SettingsViewModel.IsUpdateReadyToInstall)
                or nameof(ViewModels.SettingsViewModel.UpdateAvailableVersion))
            {
                DispatcherQueue.TryEnqueue(() => UpdateUpdateIndicator(animate: true));
            }

            // A background download finished: offer the restart once
            if (e.PropertyName == nameof(ViewModels.SettingsViewModel.IsUpdateReadyToInstall) && vm.IsUpdateReadyToInstall &&
                vm.CanSelfUpdate && !vm.IsUpdatePromptDismissed(ready: true))
            {
                DispatcherQueue.TryEnqueue(() => ShowUpdateTip(UpdateTipKind.Ready));
            }
        };
        vm.UpdatePromptRequested += (s, e) => DispatcherQueue.TryEnqueue(() =>
        {
            // When it downloads on its own, wait and offer the restart instead of two prompts in a row
            if (vm.AutoDownloadUpdates && vm.CanSelfUpdate) return;
            ShowUpdateTip(UpdateTipKind.Available);
        });

        UpdateUpdateIndicator(animate: false);

        // Greet after an update (or report a failed one) once the library has settled
        string? notice = vm.TakeLaunchUpdateNotice();
        UpdateTipKind? launchTip = notice switch
        {
            "updated" => UpdateTipKind.Updated,
            "failed" => UpdateTipKind.Failed,
            _ => vm.IsUpdateReadyToInstall && vm.CanSelfUpdate && !vm.IsUpdatePromptDismissed(ready: true) ? UpdateTipKind.Ready : null
        };
        if (launchTip is UpdateTipKind kind)
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1200);
            timer.IsRepeating = false;
            timer.Tick += (t, _) => ShowUpdateTip(kind);
            timer.Start();
        }
    }

    private void UpdateUpdateIndicator(bool animate)
    {
        if (UpdateAvailableButton == null) return;
        var vm = SettingsVM;
        bool show = vm.IsUpdateAvailable;

        var loc = App.GetService<Services.ILocalizationService>();
        ToolTipService.SetToolTip(UpdateAvailableButton, string.Format(loc["Update_Sidebar_Tooltip"], vm.UpdateAvailableVersion));
        if (UpdateButtonIcon != null)
        {
            // Download arrow while it's on the server, restart arrow once it's ready
            UpdateButtonIcon.Glyph = vm.IsUpdateReadyToInstall && vm.CanSelfUpdate ? "\uE777" : "\uE896";
        }

        if (show == _isUpdateIndicatorShown && animate) return;
        _isUpdateIndicatorShown = show;
        _updateIndicatorStoryboard?.Stop();

        if (!animate || Helpers.Motion.IsReduced)
        {
            UpdateAvailableButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            UpdateAvailableButton.Opacity = show ? 1 : 0;
            UpdateButtonScale.ScaleX = UpdateButtonScale.ScaleY = show ? 1 : 0.6;
            return;
        }

        UpdateAvailableButton.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(show ? 360 : 160);
        EasingFunctionBase ease = show
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            : new CubicEase { EasingMode = EasingMode.EaseIn };

        var fade = new DoubleAnimation { To = show ? 1 : 0, Duration = TimeSpan.FromMilliseconds(show ? 200 : 160) };
        Storyboard.SetTarget(fade, UpdateAvailableButton);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
        {
            var scale = new DoubleAnimation { To = show ? 1 : 0.6, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(scale, UpdateButtonScale);
            Storyboard.SetTargetProperty(scale, axis);
            sb.Children.Add(scale);
        }
        sb.Completed += (s, e) =>
        {
            if (!_isUpdateIndicatorShown) UpdateAvailableButton.Visibility = Visibility.Collapsed;
        };
        _updateIndicatorStoryboard = sb;
        sb.Begin();
    }

    private void ShowUpdateTip(UpdateTipKind kind)
    {
        if (UpdateTeachingTip == null) return;
        var vm = SettingsVM;
        var loc = App.GetService<Services.ILocalizationService>();
        _updateTipKind = kind;

        string version = vm.UpdateAvailableVersion;
        switch (kind)
        {
            case UpdateTipKind.Available:
                UpdateTeachingTip.Title = loc["Update_Prompt_Title"];
                UpdateTeachingTip.Subtitle = string.Format(loc["Update_Prompt_Body"], version);
                UpdateTeachingTip.ActionButtonContent = loc["Update_Prompt_View"];
                UpdateTeachingTip.CloseButtonContent = loc["Update_Prompt_Later"];
                break;
            case UpdateTipKind.Ready:
                UpdateTeachingTip.Title = loc["Update_Prompt_Title"];
                UpdateTeachingTip.Subtitle = string.Format(loc["Update_Prompt_BodyReady"], version);
                UpdateTeachingTip.ActionButtonContent = loc["Update_Prompt_Restart"];
                UpdateTeachingTip.CloseButtonContent = loc["Update_Prompt_Later"];
                break;
            case UpdateTipKind.Updated:
                UpdateTeachingTip.Title = loc["Update_Updated_Title"];
                UpdateTeachingTip.Subtitle = string.Format(loc["Update_Updated_Body"], vm.CurrentVersionTag);
                UpdateTeachingTip.ActionButtonContent = loc["Update_Updated_Notes"];
                UpdateTeachingTip.CloseButtonContent = loc["Update_Prompt_Close"];
                break;
            case UpdateTipKind.Failed:
                UpdateTeachingTip.Title = loc["Update_Failed_Title"];
                UpdateTeachingTip.Subtitle = string.Format(loc["Update_Failed_Body"], vm.CurrentVersionTag);
                UpdateTeachingTip.ActionButtonContent = loc["Update_Prompt_View"];
                UpdateTeachingTip.CloseButtonContent = loc["Update_Prompt_Close"];
                break;
        }

        // Point at the sidebar icon when it's on screen, otherwise float in the corner
        FrameworkElement? target = null;
        if (ViewModel.IsSidebarOpen)
        {
            if (UpdateAvailableButton.Visibility == Visibility.Visible) target = UpdateAvailableButton;
            else if (kind is UpdateTipKind.Updated or UpdateTipKind.Failed) target = SettingsNavButton;
        }
        UpdateTeachingTip.Target = target;
        UpdateTeachingTip.PreferredPlacement = target != null ? TeachingTipPlacementMode.RightBottom : TeachingTipPlacementMode.BottomRight;
        UpdateTeachingTip.IsOpen = true;
    }

    private void OnUpdateAvailableButtonClick(object sender, RoutedEventArgs e)
    {
        var vm = SettingsVM;
        ShowUpdateTip(vm.IsUpdateReadyToInstall && vm.CanSelfUpdate ? UpdateTipKind.Ready : UpdateTipKind.Available);
    }

    private async void OnUpdateTipActionClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        var vm = SettingsVM;
        switch (_updateTipKind)
        {
            case UpdateTipKind.Ready:
                await vm.InstallUpdateAsync();
                break;
            case UpdateTipKind.Updated:
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://github.com/k25jura/Reeled/releases/tag/{vm.CurrentVersionTag}"));
                break;
            default:
                vm.PendingSection = "UpdateSection";
                ViewModel.OpenSettings();
                break;
        }
    }

    private void OnUpdateTipCloseClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        if (_updateTipKind is UpdateTipKind.Available or UpdateTipKind.Ready)
        {
            SettingsVM.DismissUpdatePrompt(ready: _updateTipKind == UpdateTipKind.Ready);
        }
    }

    private void OnItemPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            ResetElementScale(el);
        }
    }

    private void OnClipCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            AnimateElementScale(el, 0.98, 80);
        }
    }

    private void OnClipCardPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            AnimateElementScale(el, 1.0, 160);
        }
    }

    private static void AnimateElementClickPulse(UIElement element, double targetScale = 0.96)
    {
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        Microsoft.UI.Xaml.Media.ScaleTransform scale;
        if (element.RenderTransform is Microsoft.UI.Xaml.Media.ScaleTransform st)
        {
            scale = st;
        }
        else
        {
            scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            element.RenderTransform = scale;
        }

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.CubicEase
        {
            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
        };

        var animX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animX.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
        {
            Value = scale.ScaleX,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animX.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = targetScale,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)),
            EasingFunction = easeOut
        });
        animX.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            EasingFunction = easeOut
        });

        var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
        {
            Value = scale.ScaleY,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = targetScale,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)),
            EasingFunction = easeOut
        });
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            EasingFunction = easeOut
        });

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animX, scale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, scale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "ScaleY");
        Helpers.Motion.Begin(sb);
    }

    private static void ResetElementScale(UIElement element)
    {
        if (element.RenderTransform is Microsoft.UI.Xaml.Media.ScaleTransform st && (st.ScaleX != 1.0 || st.ScaleY != 1.0))
        {
            AnimateElementScale(element, 1.0, 120);
        }
    }

    private static void AnimateElementScale(UIElement element, double targetScale, int durationMs)
    {
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        Microsoft.UI.Xaml.Media.ScaleTransform scale;
        if (element.RenderTransform is Microsoft.UI.Xaml.Media.ScaleTransform st)
        {
            scale = st;
        }
        else
        {
            scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            element.RenderTransform = scale;
        }

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
        {
            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
        };

        var animX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };
        var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animX, scale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, scale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "ScaleY");
        Helpers.Motion.Begin(sb);
    }

    private async void OnDirectoryTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        DirectoryNode? targetNode = null;
        if (args.InvokedItem is DirectoryNode dirNode)
        {
            targetNode = dirNode;
        }
        else if (args.InvokedItem is TreeViewItem tvi && tvi.DataContext is DirectoryNode dn)
        {
            targetNode = dn;
        }

        if (targetNode != null)
        {
            sender.SelectedItem = targetNode;
            bool isAlreadySelected = (ViewModel.CurrentSection == NavigationSection.Folder && ViewModel.SelectedDirectory == targetNode);

            if (targetNode.HasSubDirectories)
            {
                bool isExpanded = targetNode.IsExpanded && !_collapsingNodes.Contains(targetNode);
                if (isAlreadySelected)
                {
                    if (isExpanded)
                    {
                        _ = CollapseDirectoryWithAnimationAsync(targetNode);
                    }
                    else
                    {
                        ExpandDirectoryWithAnimation(targetNode);
                    }
                }
                else
                {
                    if (!isExpanded)
                    {
                        ExpandDirectoryWithAnimation(targetNode);
                    }
                }
            }

            if (!isAlreadySelected)
            {
                await ViewModel.SelectDirectoryAsync(targetNode);
                ScrollClipsToTop();
            }
            else
            {
                ScrollClipsToTop();
            }
            UpdateActiveIndicator(animate: true);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddDirectoryAsync(App.WindowHandle);
    }

    private async void OnOpenVideoFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;

            foreach (var ext in NavigationService.SupportedVideoExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                var filePaths = new List<string>();
                foreach (var file in files)
                {
                    filePaths.Add(file.Path);
                }
                var nav = App.GetService<INavigationService>();
                if (nav != null)
                {
                    await nav.OpenVideoFilesAsync(filePaths);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] Error opening video files: {ex.Message}");
        }
    }

    private void OnClipItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameClip clip)
        {
            ViewModel.PlayClip(clip);
        }
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GameClip clip)
        {
            bool willBeFavorite = !clip.IsFavorite;
            _ = ViewModel.ToggleFavoriteAsync(clip);
            AnimateFavoriteClick(btn, willBeFavorite);
        }
    }

    private void AnimateFavoriteClick(Button btn, bool isFavorited)
    {
        if (IsReduceMotionEnabled())
        {
            return;
        }

        var sb = new Storyboard();
        var gentleEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sineEase = new SineEase { EasingMode = EasingMode.EaseInOut };

        // 1. Button Pop Bounce Scale: soft compression, gentle rise, smooth settle
        if ((btn.RenderTransform as ScaleTransform ?? btn.FindName("FavoriteScale") as ScaleTransform) is ScaleTransform scale)
        {
            var kfScaleX = new DoubleAnimationUsingKeyFrames();
            var kfScaleY = new DoubleAnimationUsingKeyFrames();

            kfScaleX.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.88, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), EasingFunction = sineEase });
            kfScaleX.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 1.15, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), EasingFunction = gentleEase });
            kfScaleX.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.98, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), EasingFunction = gentleEase });
            kfScaleX.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 1.0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), EasingFunction = gentleEase });

            kfScaleY.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.88, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), EasingFunction = sineEase });
            kfScaleY.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 1.15, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), EasingFunction = gentleEase });
            kfScaleY.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.98, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), EasingFunction = gentleEase });
            kfScaleY.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 1.0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), EasingFunction = gentleEase });

            sb.Children.Add(kfScaleX);
            sb.Children.Add(kfScaleY);
            Storyboard.SetTarget(kfScaleX, scale);
            Storyboard.SetTargetProperty(kfScaleX, "ScaleX");
            Storyboard.SetTarget(kfScaleY, scale);
            Storyboard.SetTargetProperty(kfScaleY, "ScaleY");
        }

        // 2. Pulse Ring and Sparkles when Favorited
        if (isFavorited)
        {
            if (FindVisualParentOrChild<Border>(btn, "FavoritePulseRing") is Border pulseRing &&
                pulseRing.RenderTransform is ScaleTransform pulseScale)
            {
                pulseRing.Opacity = 0.6;
                var animRingScaleX = new DoubleAnimation { From = 0.9, To = 1.45, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = gentleEase };
                var animRingScaleY = new DoubleAnimation { From = 0.9, To = 1.45, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = gentleEase };
                var animRingOpacity = new DoubleAnimation { From = 0.6, To = 0.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = gentleEase };

                sb.Children.Add(animRingScaleX);
                sb.Children.Add(animRingScaleY);
                sb.Children.Add(animRingOpacity);
                Storyboard.SetTarget(animRingScaleX, pulseScale);
                Storyboard.SetTargetProperty(animRingScaleX, "ScaleX");
                Storyboard.SetTarget(animRingScaleY, pulseScale);
                Storyboard.SetTargetProperty(animRingScaleY, "ScaleY");
                Storyboard.SetTarget(animRingOpacity, pulseRing);
                Storyboard.SetTargetProperty(animRingOpacity, "Opacity");
            }

            if (FindVisualParentOrChild<Canvas>(btn, "FavoriteSparklesCanvas") is Canvas sparkles &&
                sparkles.RenderTransform is ScaleTransform sparklesScale)
            {
                sparkles.Opacity = 0.7;
                var animSparkleScaleX = new DoubleAnimation { From = 0.85, To = 1.25, Duration = TimeSpan.FromMilliseconds(480), EasingFunction = gentleEase };
                var animSparkleScaleY = new DoubleAnimation { From = 0.85, To = 1.25, Duration = TimeSpan.FromMilliseconds(480), EasingFunction = gentleEase };
                var animSparkleOpacity = new DoubleAnimation { From = 0.7, To = 0.0, Duration = TimeSpan.FromMilliseconds(480), EasingFunction = gentleEase };

                sb.Children.Add(animSparkleScaleX);
                sb.Children.Add(animSparkleScaleY);
                sb.Children.Add(animSparkleOpacity);
                Storyboard.SetTarget(animSparkleScaleX, sparklesScale);
                Storyboard.SetTargetProperty(animSparkleScaleX, "ScaleX");
                Storyboard.SetTarget(animSparkleScaleY, sparklesScale);
                Storyboard.SetTargetProperty(animSparkleScaleY, "ScaleY");
                Storyboard.SetTarget(animSparkleOpacity, sparkles);
                Storyboard.SetTargetProperty(animSparkleOpacity, "Opacity");
            }
        }

        Helpers.Motion.Begin(sb);
    }

    private static T? FindVisualParentOrChild<T>(FrameworkElement element, string name) where T : FrameworkElement
    {
        if (element.Parent is FrameworkElement parent)
        {
            if (parent.FindName(name) is T found) return found;
            var desc = FindVisualChildByName<T>(parent, name);
            if (desc != null) return desc;
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnClipCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (this.ProtectedCursor != null)
        {
            this.ProtectedCursor = null;
        }

        if (sender is FrameworkElement element && element.DataContext is GameClip clip)
        {
            var loc = App.GetService<Services.ILocalizationService>();
            ViewModel.SelectedClip = clip;
            AnimateCardHover(element, isHovered: true);

            var flyout = new MenuFlyout();
            if (this.XamlRoot != null)
            {
                flyout.XamlRoot = this.XamlRoot;
            }
            flyout.Opening += OnFlyoutOpening;
            flyout.Opened += OnFlyoutOpened;
            flyout.Closed += (s, args) =>
            {
                AnimateCardHover(element, isHovered: false);
            };

            // Section 1: Playback
            var playItem = new MenuFlyoutItem { Text = loc?["ContextMenu_Play"] ?? "Play", Icon = new FontIcon { Glyph = "\uE768" } };
            playItem.Click += (s, args) => ViewModel.PlayClip(clip);
            flyout.Items.Add(playItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 2: Clip Info & Management
            var infoItem = new MenuFlyoutItem { Text = loc?["ContextMenu_ClipInfo"] ?? "Clip Information", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += async (s, args) => await ShowClipInfoDialogAsync(clip);
            flyout.Items.Add(infoItem);

            string favText = clip.IsFavorite 
                ? (loc?["ContextMenu_RemoveFromFavorites"] ?? "Remove from Favorites") 
                : (loc?["ContextMenu_AddToFavorites"] ?? "Add to Favorites");
            var favItem = new MenuFlyoutItem
            {
                Text = favText,
                Icon = new FontIcon { Glyph = clip.IsFavorite ? "\uEB51" : "\uEB52" }
            };
            var favBtn = (element.FindName("FavoriteButton") as Button) ?? FindVisualChildByName<Button>(element, "FavoriteButton");
            favItem.Click += (s, args) =>
            {
                bool willBeFavorite = !clip.IsFavorite;
                _ = ViewModel.ToggleFavoriteAsync(clip);
                if (favBtn != null)
                {
                    AnimateFavoriteClick(favBtn, willBeFavorite);
                }
            };
            flyout.Items.Add(favItem);

            // Rename in App (Non-destructive)
            var renameInAppItem = new MenuFlyoutItem 
            { 
                Text = loc?["ContextMenu_RenameInApp"] ?? "Rename in App", 
                Icon = new FontIcon { Glyph = "\uE8AC" } 
            };
            renameInAppItem.Click += async (s, args) => await ShowLocalRenameDialogAsync(clip);
            flyout.Items.Add(renameInAppItem);

            // Rename File on Disk
            var renameDiskItem = new MenuFlyoutItem 
            { 
                Text = loc?["ContextMenu_RenameDisk"] ?? "Rename File on Disk", 
                Icon = new FontIcon { Glyph = "\uE8A7" } 
            };
            renameDiskItem.Click += async (s, args) => await ShowRenameDialogAsync(clip);
            flyout.Items.Add(renameDiskItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 3: File Location & System
            var explorerItem = new MenuFlyoutItem { Text = loc?["ContextMenu_RevealExplorer"] ?? "Reveal in File Explorer", Icon = new FontIcon { Glyph = "\uEC50" } };
            explorerItem.Click += (s, args) => ViewModel.OpenInExplorer(clip);
            flyout.Items.Add(explorerItem);

            var copyItem = new MenuFlyoutItem { Text = loc?["ContextMenu_CopyPath"] ?? "Copy File Path", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += (s, args) => ViewModel.CopyPath(clip);
            flyout.Items.Add(copyItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 4: Destructive
            var deleteItem = new MenuFlyoutItem { Text = loc?["ContextMenu_DeleteRecycle"] ?? "Delete to Recycle Bin", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += async (s, args) => await ShowDeleteConfirmDialogAsync(clip);
            flyout.Items.Add(deleteItem);

            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    private Brush GetThemeResourceBrush(string resourceKey)
    {
        var activeTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;
        string themeKey = activeTheme == ElementTheme.Light ? "Light" : "Dark";
        foreach (var md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(themeKey, out var dict) && dict is ResourceDictionary rd && rd.TryGetValue(resourceKey, out var val) && val is Brush b)
            {
                return b;
            }
        }

        if (Application.Current.Resources.TryGetValue(resourceKey, out var fallback) && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private async System.Threading.Tasks.Task ShowClipInfoDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;
        var panel = new StackPanel { Spacing = 12, MinWidth = 340, MaxWidth = 440, RequestedTheme = currentTheme };

        // File name
        var nameBlock = new StackPanel { Spacing = 2 };
        nameBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_FileName"] ?? "File Name", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        nameBlock.Children.Add(new TextBlock { Text = clip.FileName, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(nameBlock);

        // Grid for 2-column info (Duration, File Size)
        var grid1 = new Grid { ColumnSpacing = 16 };
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var durBlock = new StackPanel { Spacing = 2 };
        durBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Duration"] ?? "Duration", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        durBlock.Children.Add(new TextBlock { Text = clip.FormattedDuration, FontSize = 13 });
        Grid.SetColumn(durBlock, 0);
        grid1.Children.Add(durBlock);

        var sizeBlock = new StackPanel { Spacing = 2 };
        sizeBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_FileSize"] ?? "File Size", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        sizeBlock.Children.Add(new TextBlock { Text = clip.FormattedFileSize, FontSize = 13 });
        Grid.SetColumn(sizeBlock, 1);
        grid1.Children.Add(sizeBlock);
        panel.Children.Add(grid1);

        // Grid for 2-column info (Resolution/FPS, Moments)
        var grid2 = new Grid { ColumnSpacing = 16 };
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resBlock = new StackPanel { Spacing = 2 };
        resBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Resolution"] ?? "Resolution", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        string resText = clip.VideoWidth > 0 ? $"{clip.VideoWidth} x {clip.VideoHeight}" + (clip.Framerate > 0 ? $" ({clip.Framerate:F0} fps)" : "") : (loc?["Dialog_ClipInfo_Unknown"] ?? "Unknown");
        resBlock.Children.Add(new TextBlock { Text = resText, FontSize = 13 });
        Grid.SetColumn(resBlock, 0);
        grid2.Children.Add(resBlock);

        var momentBlock = new StackPanel { Spacing = 2 };
        momentBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Moments"] ?? "Saved Moments", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        string momentsText = clip.Bookmarks.Count > 0 
            ? string.Format(loc?["Dialog_ClipInfo_MomentsMarked"] ?? "{0} marked", clip.Bookmarks.Count)
            : (loc?["Dialog_ClipInfo_None"] ?? "None");
        momentBlock.Children.Add(new TextBlock { Text = momentsText, FontSize = 13 });
        Grid.SetColumn(momentBlock, 1);
        grid2.Children.Add(momentBlock);
        panel.Children.Add(grid2);

        // Grid for 2-column info (Date Modified on left, File Properties action button on right)
        var grid3 = new Grid { ColumnSpacing = 16 };
        grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dateBlock = new StackPanel { Spacing = 2 };
        dateBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_DateModified"] ?? "Date Modified", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        dateBlock.Children.Add(new TextBlock { Text = clip.FormattedDate, FontSize = 13 });
        Grid.SetColumn(dateBlock, 0);
        grid3.Children.Add(dateBlock);

        var openPropsBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE946", FontSize = 12 },
                    new TextBlock { Text = loc?["Dialog_ClipInfo_OpenProperties"] ?? "Properties", FontSize = 12 }
                }
            },
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        openPropsBtn.Click += (s, args) => ViewModel.OpenFileProperties(clip);
        Grid.SetColumn(openPropsBtn, 1);
        grid3.Children.Add(openPropsBtn);
        panel.Children.Add(grid3);

        // File path
        var pathBlock = new StackPanel { Spacing = 2 };
        pathBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Location"] ?? "Location", FontSize = 11, Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush") });
        pathBlock.Children.Add(new TextBlock { Text = clip.FilePath, FontSize = 12, Foreground = GetThemeResourceBrush("TextFillColorTertiaryBrush"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        panel.Children.Add(pathBlock);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = currentTheme,
            Title = loc?["Dialog_ClipInfo_Title"] ?? "Clip Information",
            Content = panel,
            CloseButtonText = loc?["Dialog_ClipInfo_Close"] ?? "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task ShowLocalRenameDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;

        var panel = new StackPanel { Spacing = 12, MinWidth = 360, MaxWidth = 440, RequestedTheme = currentTheme };

        var descBlock = new TextBlock
        {
            Text = loc?["Dialog_RenameInApp_Desc"] ?? "Set a custom display title for this clip in Reeled. The original file on disk remains untouched.",
            FontSize = 12,
            Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(descBlock);

        var origBlock = new StackPanel { Spacing = 2 };
        origBlock.Children.Add(new TextBlock
        {
            Text = loc?["Dialog_RenameInApp_OriginalFile"] ?? "File on disk:",
            FontSize = 11,
            Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush")
        });
        origBlock.Children.Add(new TextBlock
        {
            Text = clip.FileName,
            FontSize = 12,
            Foreground = GetThemeResourceBrush("TextFillColorTertiaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(origBlock);

        var textBox = new TextBox
        {
            Text = !string.IsNullOrWhiteSpace(clip.CustomTitle) ? clip.CustomTitle : System.IO.Path.GetFileNameWithoutExtension(clip.FileName),
            PlaceholderText = loc?["Dialog_RenameInApp_Placeholder"] ?? "Enter custom clip title...",
            RequestedTheme = currentTheme
        };
        textBox.Loaded += (s, e) => textBox.SelectAll();
        panel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = currentTheme,
            Title = loc?["Dialog_RenameInApp_Title"] ?? "Rename Clip (In-App)",
            Content = panel,
            PrimaryButtonText = loc?["Dialog_RenameInApp_Save"] ?? "Save",
            SecondaryButtonText = !string.IsNullOrWhiteSpace(clip.CustomTitle) ? (loc?["Dialog_RenameInApp_Reset"] ?? "Reset to Original") : null,
            CloseButtonText = loc?["Dialog_RenameInApp_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            string newTitle = textBox.Text.Trim();
            await ViewModel.SetClipCustomTitleAsync(clip, newTitle);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ViewModel.SetClipCustomTitleAsync(clip, null);
        }
    }

    private async System.Threading.Tasks.Task ShowRenameDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;
        var textBox = new TextBox
        {
            Text = System.IO.Path.GetFileNameWithoutExtension(clip.FileName),
            RequestedTheme = currentTheme
        };
        textBox.Loaded += (s, e) => textBox.SelectAll();

        var errorText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetThemeResourceBrush("SystemFillColorCriticalBrush"),
            Visibility = Visibility.Collapsed
        };
        textBox.TextChanged += (s, e) => errorText.Visibility = Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 8, MinWidth = 340, RequestedTheme = currentTheme };
        panel.Children.Add(textBox);
        panel.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = currentTheme,
            Title = loc?["Dialog_Rename_Title"] ?? "Rename Clip",
            Content = panel,
            PrimaryButtonText = loc?["Dialog_Rename_Confirm"] ?? "Rename",
            CloseButtonText = loc?["Dialog_Rename_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        // Validate and rename while the dialog is still open so mistakes can be corrected in place
        dialog.PrimaryButtonClick += (s, args) =>
        {
            string newName = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                args.Cancel = true;
                return;
            }

            string? error = null;
            if (newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                error = loc?["Dialog_Rename_InvalidChars"] ?? "A file name can't contain any of these characters: \\ / : * ? \" < > |";
            }
            else if (!ViewModel.RenameClipInternal(clip, newName))
            {
                error = loc?["Dialog_Rename_Failed"] ?? "Couldn't rename the file.";
            }

            if (error != null)
            {
                errorText.Text = error;
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string message)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme,
            Title = loc?["Dialog_Error_Title"] ?? "Something went wrong",
            Content = message,
            CloseButtonText = loc?["Dialog_Error_Close"] ?? "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task ShowDeleteConfirmDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var currentTheme = this.ActualTheme != ElementTheme.Default ? this.ActualTheme : App.CurrentTheme;
        string message = string.Format(loc?["Dialog_Delete_Message"] ?? "Are you sure you want to move '{0}' to the Windows Recycle Bin?", clip.FileName);
        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = currentTheme,
            Title = loc?["Dialog_Delete_Title"] ?? "Delete to Recycle Bin?",
            Content = message,
            PrimaryButtonText = loc?["Dialog_Delete_Confirm"] ?? "Delete",
            CloseButtonText = loc?["Dialog_Delete_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !ViewModel.DeleteClip(clip))
        {
            await ShowErrorDialogAsync(loc?["Dialog_Delete_Failed"] ?? "Couldn't move the file to the Recycle Bin.");
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
