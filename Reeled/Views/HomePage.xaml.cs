using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.GetService<HomeViewModel>();
        InitializeComponent();

        _sidebarWidth = ViewModel.SidebarWidth >= 200 ? ViewModel.SidebarWidth : 316.0;

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

            if (ClipsGridView != null)
            {
                ClipsGridView.RequestedTheme = ActualTheme;
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
            UpdateLogo(ActualTheme);
            UpdateNavTabVisuals();
            if (ClipsGridView != null)
            {
                ClipsGridView.RequestedTheme = ActualTheme;
            }
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
                }
            }
        };
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
            if (FoldersHeaderNavText != null) FoldersHeaderNavText.Text = loc["Nav_Folders"];
            if (FoldersAddNavText != null) FoldersAddNavText.Text = loc["Nav_AddFolder"];
            if (SettingsNavText != null) SettingsNavText.Text = loc["Nav_Settings"];

            // Tooltips
            if (CollapseSidebarButton != null) ToolTipService.SetToolTip(CollapseSidebarButton, loc["Tooltip_CollapseSidebar"]);
            if (ShowSidebarButton != null) ToolTipService.SetToolTip(ShowSidebarButton, loc["Tooltip_ShowSidebar"]);
            if (AddWatchFolderButton != null) ToolTipService.SetToolTip(AddWatchFolderButton, loc["Tooltip_AddWatchFolder"]);
            if (RefreshClipsButton != null) ToolTipService.SetToolTip(RefreshClipsButton, loc["Tooltip_RefreshClips"]);
            if (ExplorerHyperlinkButton != null) ToolTipService.SetToolTip(ExplorerHyperlinkButton, loc["Tooltip_OpenInExplorer"]);

            // Search Box Placeholder
            if (ClipsSearchBox != null) ClipsSearchBox.PlaceholderText = loc["Search_Placeholder"];

            // Sort Dropdown Items
            if (SortNewestItem != null) SortNewestItem.Content = loc["Sort_NewestDate"];
            if (SortOldestItem != null) SortOldestItem.Content = loc["Sort_OldestDate"];
            if (SortNameAZItem != null) SortNameAZItem.Content = loc["Sort_TitleAZ"];
            if (SortDurationItem != null) SortDurationItem.Content = loc["Sort_Duration"];
            if (SortFileSizeItem != null) SortFileSizeItem.Content = loc["Sort_FileSize"];

            // Folders Empty Fallback Prompt
            if (FoldersFallbackTitleText != null) FoldersFallbackTitleText.Text = loc["Folders_FallbackTitle"];
            if (FoldersFallbackSubtitleText != null) FoldersFallbackSubtitleText.Text = loc["Folders_FallbackSubtitle"];
            if (FoldersFallbackButton != null) FoldersFallbackButton.Content = loc["Folders_ChooseFolder"];
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

            // 3. Create dummy elements inside the invisible warmup canvas to pre-tick Storyboards
            if (AnimationWarmupCanvas != null)
            {
                var dummyTarget = new Border
                {
                    Width = 36,
                    Height = 36,
                    Opacity = 0.0,
                    RenderTransform = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(),
                            new ScaleTransform(),
                            new RotateTransform()
                        }
                    }
                };
                AnimationWarmupCanvas.Children.Add(dummyTarget);

                var dummyTvi = new TreeViewItem
                {
                    Style = (Style)Resources["CustomTreeViewItemStyle"],
                    Opacity = 0.0,
                    Height = 0.0
                };
                AnimationWarmupCanvas.Children.Add(dummyTvi);
                dummyTvi.ApplyTemplate();

                // Warm up VisualStateManager transitions
                VisualStateManager.GoToState(dummyTvi, "PointerOver", true);
                VisualStateManager.GoToState(dummyTvi, "Selected", true);
                VisualStateManager.GoToState(dummyTvi, "Normal", true);
                VisualStateManager.GoToState(dummyTvi, "Expanded", true);
                VisualStateManager.GoToState(dummyTvi, "Collapsed", true);

                // Warm up micro-storyboards for transform, opacity, height, and keyframes
                var warmupSb = new Storyboard();
                var dur = TimeSpan.FromMilliseconds(1);

                // Height dependent animation warmup
                var animH = new DoubleAnimation { From = 0, To = 36, Duration = dur, EasingFunction = easeOut, EnableDependentAnimation = true };
                Storyboard.SetTarget(animH, dummyTarget);
                Storyboard.SetTargetProperty(animH, "Height");
                warmupSb.Children.Add(animH);

                // Translate Y keyframe animation warmup
                var trans = ((TransformGroup)dummyTarget.RenderTransform).Children[0] as TranslateTransform;
                var animY = new DoubleAnimationUsingKeyFrames();
                animY.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = -10, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
                animY.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(dur), EasingFunction = easeOut });
                Storyboard.SetTarget(animY, trans);
                Storyboard.SetTargetProperty(animY, "Y");
                warmupSb.Children.Add(animY);

                // Opacity keyframe animation warmup
                var animOp = new DoubleAnimationUsingKeyFrames();
                animOp.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
                animOp.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.01, KeyTime = KeyTime.FromTimeSpan(dur), EasingFunction = easeOut });
                Storyboard.SetTarget(animOp, dummyTarget);
                Storyboard.SetTargetProperty(animOp, "Opacity");
                warmupSb.Children.Add(animOp);

                // Scale pulse keyframe animation warmup
                var scale = ((TransformGroup)dummyTarget.RenderTransform).Children[1] as ScaleTransform;
                var animScale = new DoubleAnimationUsingKeyFrames();
                animScale.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 1.0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
                animScale.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0.98, KeyTime = KeyTime.FromTimeSpan(dur), EasingFunction = cubicEaseOut });
                Storyboard.SetTarget(animScale, scale);
                Storyboard.SetTargetProperty(animScale, "ScaleX");
                warmupSb.Children.Add(animScale);

                // Rotate chevron animation warmup
                var rotate = ((TransformGroup)dummyTarget.RenderTransform).Children[2] as RotateTransform;
                var animRot = new DoubleAnimation { From = 0, To = 90, Duration = dur, EasingFunction = cubicEaseOut };
                Storyboard.SetTarget(animRot, rotate);
                Storyboard.SetTargetProperty(animRot, "Angle");
                warmupSb.Children.Add(animRot);

                warmupSb.Completed += (s, e) =>
                {
                    warmupSb.Stop();
                    AnimationWarmupCanvas.Children.Clear();
                };

                warmupSb.Begin();
            }
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

        btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
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
                sb.Begin();
            }
        }

        if (isSelected)
        {
            Brush selectedBrush = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

            icon.Foreground = selectedBrush;
            text.Foreground = selectedBrush;
            text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        else
        {
            var defaultTextBrush = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 75))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210));

            icon.Foreground = defaultTextBrush;
            text.Foreground = defaultTextBrush;
            text.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
    }

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

    private void UpdateActiveIndicator(bool animate = true)
    {
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
            fadeInSb.Begin();
        };

        _indicatorStoryboard = fadeOutSb;
        fadeOutSb.Begin();
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
        if (DirectoriesTreeView != null)
        {
            DirectoriesTreeView.LayoutUpdated += OnDirectoriesTreeViewLayoutUpdated;
            DirectoriesTreeView.PointerWheelChanged += (s, args) => UpdateFolderIndicatorPosition();
        }
    }

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
        if (sender is TreeViewItem tvi && tvi.DataContext is DirectoryNode node)
        {
            if (!node.IsWatchRoot)
            {
                tvi.Opacity = 0.0;
                tvi.Height = 0.0;
                if (tvi.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                {
                    tvi.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { Y = -10.0 };
                }
            }
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
            sb.Begin();
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
        moveSb.Begin();
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
        sb.Begin();
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
                AnimateSidebar(isOpen: false);
            }
        }
        else if (e.NewSize.Width >= restoreThreshold)
        {
            if (_wasAutoCollapsed && ViewModel.IsSidebarCollapsed && !_isUserToggling)
            {
                _wasAutoCollapsed = false;
                ViewModel.IsSidebarCollapsed = false;
                AnimateSidebar(isOpen: true);
            }
        }
    }

    private void AnimateSidebar(bool isOpen, bool animate = true)
    {
        _sidebarStoryboard?.Stop();
        _sidebarStoryboard = null;

        double targetWidth = isOpen ? _sidebarWidth : 0.0;
        double targetTranslateX = isOpen ? 0.0 : -_sidebarWidth;
        double targetOpacity = isOpen ? 1.0 : 0.0;

        if (!animate)
        {
            SidebarContainer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SidebarContainer.Width = targetWidth;
            SidebarContentBorder.Width = _sidebarWidth;
            SidebarTranslate.X = targetTranslateX;
            SidebarContentBorder.Opacity = targetOpacity;
            return;
        }

        // Determine starting values before launching storyboard
        double startWidth;
        double startTranslateX;
        double startOpacity;

        if (isOpen)
        {
            SidebarContainer.Visibility = Visibility.Visible;
            SidebarContentBorder.Width = _sidebarWidth;

            startWidth = (SidebarContainer.ActualWidth > 0 && SidebarContainer.ActualWidth < _sidebarWidth)
                ? SidebarContainer.ActualWidth
                : 0.0;
            startTranslateX = (SidebarTranslate.X < 0 && SidebarTranslate.X > -_sidebarWidth)
                ? SidebarTranslate.X
                : -_sidebarWidth;
            startOpacity = (SidebarContentBorder.Opacity > 0 && SidebarContentBorder.Opacity < 1.0)
                ? SidebarContentBorder.Opacity
                : 0.0;

            SidebarContainer.Width = startWidth;
            SidebarTranslate.X = startTranslateX;
            SidebarContentBorder.Opacity = startOpacity;
        }
        else
        {
            startWidth = SidebarContainer.ActualWidth > 0 ? SidebarContainer.ActualWidth : _sidebarWidth;
            startTranslateX = SidebarTranslate.X;
            startOpacity = SidebarContentBorder.Opacity;
        }

        var sb = new Storyboard();
        var openEase = new Microsoft.UI.Xaml.Media.Animation.QuarticEase
        {
            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
        };
        var closeEase = new Microsoft.UI.Xaml.Media.Animation.QuarticEase
        {
            EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut
        };

        Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase ease = isOpen
            ? openEase
            : closeEase;

        var duration = TimeSpan.FromMilliseconds(isOpen ? 380 : 320);

        // 1. Width animation on container with EnableDependentAnimation = true
        var animWidth = new DoubleAnimation
        {
            From = startWidth,
            To = targetWidth,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animWidth, SidebarContainer);
        Storyboard.SetTargetProperty(animWidth, "Width");

        // 2. Translation animation on content border so it glides smoothly
        var animTranslate = new DoubleAnimation
        {
            From = startTranslateX,
            To = targetTranslateX,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(animTranslate, SidebarTranslate);
        Storyboard.SetTargetProperty(animTranslate, "X");

        // 3. Subtle Opacity fade in and fade out
        var animOpacity = new DoubleAnimation
        {
            From = startOpacity,
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(isOpen ? 340 : 260),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animOpacity, SidebarContentBorder);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");

        sb.Children.Add(animWidth);
        sb.Children.Add(animTranslate);
        sb.Children.Add(animOpacity);

        sb.Completed += (s, e) =>
        {
            _sidebarStoryboard = null;
            SidebarContainer.Width = targetWidth;
            SidebarTranslate.X = targetTranslateX;
            SidebarContentBorder.Opacity = targetOpacity;
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
        this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
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

    private void OnClipContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer != null && args.ItemContainer.RequestedTheme != ActualTheme)
        {
            args.ItemContainer.RequestedTheme = ActualTheme;
        }
    }

    private void OnClipCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            AnimateCardHover(card, isHovered: true);
        }
    }

    private void OnClipCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            AnimateCardHover(card, isHovered: false);
        }
    }

    private void AnimateCardHover(FrameworkElement card, bool isHovered)
    {
        // 1. Unified Outline & Background Highlight (Active only when hovered or during right-click context menu)
        if (card is Grid grid)
        {
            if (isHovered)
            {
                if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentBrush))
                {
                    grid.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)accentBrush;
                }
                if (grid.ActualTheme == ElementTheme.Light)
                {
                    grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF8, 0xF8, 0xFA));
                }
                else
                {
                    grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x22, 0x22, 0x22));
                }
            }
            else
            {
                grid.ClearValue(Grid.BorderBrushProperty);
                grid.ClearValue(Grid.BackgroundProperty);
            }
        }

        // 2. Play Button Overlay Animation (Compositor-friendly fade & scale)
        if (card.FindName("PlayOverlay") is UIElement playOverlay)
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

            var animOpacity = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = isHovered ? 1.0 : 0.0,
                Duration = TimeSpan.FromMilliseconds(isHovered ? 180 : 140),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                { 
                    EasingMode = isHovered ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn 
                }
            };
            sb.Children.Add(animOpacity);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, playOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");

            if (card.FindName("PlayOverlayScale") is Microsoft.UI.Xaml.Media.ScaleTransform scaleTransform)
            {
                var animScaleX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = isHovered ? 1.0 : 0.8,
                    Duration = TimeSpan.FromMilliseconds(isHovered ? 180 : 140),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                    { 
                        EasingMode = isHovered ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn 
                    }
                };
                var animScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = isHovered ? 1.0 : 0.8,
                    Duration = TimeSpan.FromMilliseconds(isHovered ? 180 : 140),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                    { 
                        EasingMode = isHovered ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn 
                    }
                };
                sb.Children.Add(animScaleX);
                sb.Children.Add(animScaleY);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleX, scaleTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleX, "ScaleX");
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleY, scaleTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleY, "ScaleY");
            }

            sb.Begin();
        }

        // 3. Metadata Row Animation (Compositor-friendly slide & fade)
        if (card.FindName("MetadataRow") is UIElement metadataRow)
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

            var animOpacity = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = isHovered ? 1.0 : 0.0,
                Duration = TimeSpan.FromMilliseconds(isHovered ? 180 : 140),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                { 
                    EasingMode = isHovered ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn 
                }
            };
            sb.Children.Add(animOpacity);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, metadataRow);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");

            if (card.FindName("MetadataTransform") is Microsoft.UI.Xaml.Media.TranslateTransform trans)
            {
                var animTranslateY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = isHovered ? 0.0 : 6.0,
                    Duration = TimeSpan.FromMilliseconds(isHovered ? 180 : 140),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                    { 
                        EasingMode = isHovered ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn 
                    }
                };
                sb.Children.Add(animTranslateY);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animTranslateY, trans);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animTranslateY, "Y");
            }

            sb.Begin();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.SyncDirectoriesAsync();
    }

    private async void OnHomeTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectHomeAsync();
        UpdateActiveIndicator(animate: true);
    }

    private async void OnFavoritesTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectFavoritesAsync();
        UpdateActiveIndicator(animate: true);
    }

    private async void OnSavedMomentsTabClick(object sender, RoutedEventArgs e)
    {
        DirectoriesTreeView.SelectedItem = null;
        await ViewModel.SelectSavedMomentsAsync();
        UpdateActiveIndicator(animate: true);
    }

    private readonly System.Collections.Generic.Dictionary<FrameworkElement, Storyboard> _folderStoryboards = new();

    private void StopFolderStoryboard(FrameworkElement element)
    {
        if (_folderStoryboards.TryGetValue(element, out var oldSb))
        {
            _folderStoryboards.Remove(element);
            oldSb.Stop();
            element.Opacity = 0.0;
            if (element.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
            {
                tt.Y = -12.0;
            }
        }
    }

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

    private int _expandingGeneration;
    private readonly System.Collections.Generic.Dictionary<DirectoryNode, System.Threading.CancellationTokenSource> _folderAnimationTokens = new();
    private readonly System.Collections.Generic.Dictionary<DirectoryNode, Microsoft.UI.Xaml.Media.Animation.Storyboard> _activeSpaceStoryboards = new();
    private readonly System.Collections.Generic.HashSet<DirectoryNode> _collapsingNodes = new();

    private async void OnDirectoryExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        int currentGen = ++_expandingGeneration;
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode == null) return;
        _collapsingNodes.Remove(dirNode);
        dirNode.IsExpanded = true;

        if (_folderAnimationTokens.TryGetValue(dirNode, out var existingCts))
        {
            existingCts.Cancel();
            _folderAnimationTokens.Remove(dirNode);
        }

        if (_activeSpaceStoryboards.TryGetValue(dirNode, out var runningSpaceSb))
        {
            runningSpaceSb.Stop();
            _activeSpaceStoryboards.Remove(dirNode);
        }

        var cts = new System.Threading.CancellationTokenSource();
        _folderAnimationTokens[dirNode] = cts;
        var token = cts.Token;

        var itemsToAnimate = new System.Collections.Generic.List<DirectoryNode>();
        CollectVisibleDescendants(dirNode, itemsToAnimate);

        if (itemsToAnimate.Count == 0) return;

        // Zero out opacity and height on any already-materialized containers
        foreach (var node in itemsToAnimate)
        {
            if (sender.ContainerFromItem(node) is TreeViewItem container)
            {
                StopFolderStoryboard(container);
                container.Opacity = 0.0;
                container.Height = 0.0;
                if (container.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
                {
                    tt.Y = -10.0;
                }
            }
        }

        // Fast polling to ensure containers are realized (instant on cached items, reliable on first load)
        var targetContainers = new System.Collections.Generic.List<(TreeViewItem Container, int Index)>();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            targetContainers.Clear();
            for (int i = 0; i < itemsToAnimate.Count; i++)
            {
                var node = itemsToAnimate[i];
                if (sender.ContainerFromItem(node) is TreeViewItem container)
                {
                    container.Opacity = 0.0;
                    container.Height = 0.0;
                    targetContainers.Add((container, i));
                }
            }

            if (targetContainers.Count >= itemsToAnimate.Count)
                break;

            try
            {
                await System.Threading.Tasks.Task.Delay(10, token);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return;
            }
        }

        if (token.IsCancellationRequested || currentGen != _expandingGeneration || !dirNode.IsExpanded) return;
        if (targetContainers.Count == 0) return;

        // Phase 1: Accordion Space Extension (invisible space extends downwards using Apple QuarticEase)
        var spaceSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        var spaceDuration = TimeSpan.FromMilliseconds(190);

        foreach (var (container, _) in targetContainers)
        {
            var animHeight = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 36.0,
                Duration = spaceDuration,
                EasingFunction = easeOut,
                EnableDependentAnimation = true
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animHeight, container);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animHeight, "Height");
            spaceSb.Children.Add(animHeight);
        }

        _activeSpaceStoryboards[dirNode] = spaceSb;
        spaceSb.Begin();

        try
        {
            await System.Threading.Tasks.Task.Delay(195, token);
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            spaceSb.Stop();
            _activeSpaceStoryboards.Remove(dirNode);
            return;
        }

        _activeSpaceStoryboards.Remove(dirNode);

        if (token.IsCancellationRequested || currentGen != _expandingGeneration || !dirNode.IsExpanded) return;

        // Restore standard auto-sizing
        foreach (var (container, _) in targetContainers)
        {
            container.Height = double.NaN;
        }

        // Phase 2: Cascading Folder Animation (top to bottom slide down and fade in)
        foreach (var (container, index) in targetContainers)
        {
            AnimateCascadeEntrance(container, index);
        }

        _folderAnimationTokens.Remove(dirNode);
        UpdateActiveIndicator(animate: true);
    }

    private void OnDirectoryCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        _expandingGeneration++;
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode != null)
        {
            dirNode.IsExpanded = false;
            _collapsingNodes.Remove(dirNode);
            if (_activeSpaceStoryboards.TryGetValue(dirNode, out var runningSpaceSb))
            {
                runningSpaceSb.Stop();
                _activeSpaceStoryboards.Remove(dirNode);
            }
        }
        UpdateActiveIndicator(animate: true);
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
                _collapsingNodes.Remove(node);
                node.IsExpanded = true;
            }
        }
    }

    private async System.Threading.Tasks.Task CollapseDirectoryWithAnimationAsync(DirectoryNode dirNode)
    {
        _collapsingNodes.Add(dirNode);

        if (_folderAnimationTokens.TryGetValue(dirNode, out var oldCts))
        {
            oldCts.Cancel();
        }

        if (_activeSpaceStoryboards.TryGetValue(dirNode, out var activeSpaceSb))
        {
            activeSpaceSb.Stop();
            _activeSpaceStoryboards.Remove(dirNode);
        }

        var cts = new System.Threading.CancellationTokenSource();
        _folderAnimationTokens[dirNode] = cts;
        var token = cts.Token;

        var itemsToAnimate = new System.Collections.Generic.List<DirectoryNode>();
        CollectVisibleDescendants(dirNode, itemsToAnimate);

        var targetContainers = new System.Collections.Generic.List<TreeViewItem>();
        if (DirectoriesTreeView != null)
        {
            foreach (var node in itemsToAnimate)
            {
                if (DirectoriesTreeView.ContainerFromItem(node) is TreeViewItem container)
                {
                    targetContainers.Add(container);
                }
            }
        }

        // Immediately rotate parent chevron to collapsed state
        if (DirectoriesTreeView?.ContainerFromItem(dirNode) is TreeViewItem parentTvi)
        {
            VisualStateManager.GoToState(parentTvi, "Collapsed", true);
        }

        // If selected item is inside this collapsing branch, hide indicator immediately
        if (ViewModel.SelectedDirectory != null && itemsToAnimate.Contains(ViewModel.SelectedDirectory))
        {
            AnimateIndicatorVisibility(false, animate: true);
        }

        if (targetContainers.Count > 0 && DirectoriesTreeView != null)
        {
            // Phase 1: Cascading Folder Exit in REVERSE order (bottom to top)
            int count = targetContainers.Count;
            for (int i = 0; i < count; i++)
            {
                int reverseIndex = count - 1 - i; // Bottom item has reverseIndex = 0
                AnimateCascadeExit(targetContainers[i], reverseIndex);
            }

            int exitWaitMs = Math.Min((count - 1) * 18 + 140, 260);
            try
            {
                await System.Threading.Tasks.Task.Delay(exitWaitMs, token);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                _collapsingNodes.Remove(dirNode);
                return;
            }

            if (token.IsCancellationRequested)
            {
                _collapsingNodes.Remove(dirNode);
                return;
            }

            // Phase 2: Accordion Space Collapse (space shrinks upward from 36 to 0 using Apple QuarticEase)
            var spaceSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var easeInOut = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut };
            var spaceDuration = TimeSpan.FromMilliseconds(180);

            foreach (var container in targetContainers)
            {
                var animHeight = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = container.ActualHeight > 0 ? container.ActualHeight : 36.0,
                    To = 0.0,
                    Duration = spaceDuration,
                    EasingFunction = easeInOut,
                    EnableDependentAnimation = true
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animHeight, container);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animHeight, "Height");
                spaceSb.Children.Add(animHeight);
            }

            _activeSpaceStoryboards[dirNode] = spaceSb;
            spaceSb.Begin();

            try
            {
                await System.Threading.Tasks.Task.Delay(185, token);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                spaceSb.Stop();
                _activeSpaceStoryboards.Remove(dirNode);
                _collapsingNodes.Remove(dirNode);
                return;
            }

            _activeSpaceStoryboards.Remove(dirNode);

            if (token.IsCancellationRequested)
            {
                _collapsingNodes.Remove(dirNode);
                return;
            }
        }

        dirNode.IsExpanded = false;
        _collapsingNodes.Remove(dirNode);
        _folderAnimationTokens.Remove(dirNode);

        foreach (var container in targetContainers)
        {
            StopFolderStoryboard(container);
            container.Opacity = 0.0;
            container.Height = double.NaN;
            if (container.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
            {
                tt.Y = -10.0;
            }
        }

        UpdateActiveIndicator(animate: true);
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

    private void AnimateCascadeEntrance(FrameworkElement element, int index)
    {
        StopFolderStoryboard(element);

        Microsoft.UI.Xaml.Media.TranslateTransform trans;
        if (element.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform existingTt)
        {
            trans = existingTt;
        }
        else
        {
            trans = new Microsoft.UI.Xaml.Media.TranslateTransform();
            element.RenderTransform = trans;
        }

        double slideDistance = -(10.0 + Math.Min(index * 0.8, 6.0));
        trans.Y = slideDistance;
        element.Opacity = 0.0;

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 10) * 20);
        var duration = TimeSpan.FromMilliseconds(180);
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        // 1. Y translation using Apple QuarticEase decelerate curve
        var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
        {
            Value = slideDistance,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        if (delay > TimeSpan.Zero)
        {
            animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                Value = slideDistance,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay)
            });
        }
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = 0.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay + duration),
            EasingFunction = easeOut
        });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, trans);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "Y");

        // 2. Opacity fade in using Apple QuarticEase
        var animOp = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
        {
            Value = 0.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        if (delay > TimeSpan.Zero)
        {
            animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                Value = 0.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay)
            });
        }
        animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(160)),
            EasingFunction = easeOut
        });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOp, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOp, "Opacity");

        sb.Children.Add(animY);
        sb.Children.Add(animOp);

        _folderStoryboards[element] = sb;

        sb.Completed += (s, e) =>
        {
            if (_folderStoryboards.TryGetValue(element, out var currentSb) && currentSb == sb)
            {
                _folderStoryboards.Remove(element);
                sb.Stop();
                element.Opacity = 1.0;
                trans.Y = 0.0;
            }
        };

        sb.Begin();
    }

    private void AnimateCascadeExit(FrameworkElement element, int index)
    {
        StopFolderStoryboard(element);

        Microsoft.UI.Xaml.Media.TranslateTransform trans;
        if (element.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform existingTt)
        {
            trans = existingTt;
        }
        else
        {
            trans = new Microsoft.UI.Xaml.Media.TranslateTransform();
            element.RenderTransform = trans;
        }

        trans.Y = 0.0;
        element.Opacity = 1.0;

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 10) * 18);
        var duration = TimeSpan.FromMilliseconds(140);
        var easeInOut = new Microsoft.UI.Xaml.Media.Animation.QuarticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut };

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        // 1. Y translation upward slide using QuarticEase EaseInOut
        var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
        {
            Value = 0.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        if (delay > TimeSpan.Zero)
        {
            animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                Value = 0.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay)
            });
        }
        animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = -10.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay + duration),
            EasingFunction = easeInOut
        });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, trans);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "Y");

        // 2. Opacity fade out using QuarticEase EaseInOut
        var animOp = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
        {
            Value = 1.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
        });
        if (delay > TimeSpan.Zero)
        {
            animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay)
            });
        }
        animOp.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            Value = 0.0,
            KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(delay + duration),
            EasingFunction = easeInOut
        });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOp, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOp, "Opacity");

        sb.Children.Add(animY);
        sb.Children.Add(animOp);

        _folderStoryboards[element] = sb;

        sb.Completed += (s, e) =>
        {
            if (_folderStoryboards.TryGetValue(element, out var currentSb) && currentSb == sb)
            {
                _folderStoryboards.Remove(element);
                sb.Stop();
                element.Opacity = 0.0;
                trans.Y = -10.0;
            }
        };

        sb.Begin();
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
        sb.Begin();
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
        sb.Begin();
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
            if (targetNode.HasSubDirectories)
            {
                bool isExpanded = targetNode.IsExpanded && !_collapsingNodes.Contains(targetNode);
                if (isExpanded)
                {
                    _ = CollapseDirectoryWithAnimationAsync(targetNode);
                }
                else
                {
                    _collapsingNodes.Remove(targetNode);
                    targetNode.IsExpanded = true;
                }
            }
            sender.SelectedItem = targetNode;
            if (ViewModel.SelectedDirectory != targetNode || ViewModel.CurrentSection != NavigationSection.Folder)
            {
                await ViewModel.SelectDirectoryAsync(targetNode);
            }
            UpdateActiveIndicator(animate: true);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddDirectoryAsync(App.WindowHandle);
    }

    private void OnClipItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameClip clip)
        {
            ViewModel.PlayClip(clip);
        }
    }

    private void OnClipCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && IsDescendantOf<Button>(dep))
        {
            return;
        }
        if (sender is FrameworkElement element && element.DataContext is GameClip clip)
        {
            ViewModel.PlayClip(clip);
        }
    }

    private void OnClipCardDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && IsDescendantOf<Button>(dep))
        {
            return;
        }
        if (sender is FrameworkElement element && element.DataContext is GameClip clip)
        {
            ViewModel.PlayClip(clip);
        }
    }

    private static bool IsDescendantOf<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is T) return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GameClip clip)
        {
            _ = ViewModel.ToggleFavoriteAsync(clip);
        }
    }

    private void OnClipCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GameClip clip)
        {
            var loc = App.GetService<Services.ILocalizationService>();
            ViewModel.SelectedClip = clip;
            AnimateCardHover(element, isHovered: true);

            var flyout = new MenuFlyout();
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
            favItem.Click += (s, args) => _ = ViewModel.ToggleFavoriteAsync(clip);
            flyout.Items.Add(favItem);

            var renameItem = new MenuFlyoutItem { Text = loc?["ContextMenu_Rename"] ?? "Rename", Icon = new FontIcon { Glyph = "\uE8AC" } };
            renameItem.Click += async (s, args) => await ShowRenameDialogAsync(clip);
            flyout.Items.Add(renameItem);

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

    private async System.Threading.Tasks.Task ShowClipInfoDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var panel = new StackPanel { Spacing = 12, MinWidth = 340, MaxWidth = 440 };

        // File name
        var nameBlock = new StackPanel { Spacing = 2 };
        nameBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_FileName"] ?? "File Name", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        nameBlock.Children.Add(new TextBlock { Text = clip.FileName, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(nameBlock);

        // Grid for 2-column info (Duration, File Size)
        var grid1 = new Grid { ColumnSpacing = 16 };
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var durBlock = new StackPanel { Spacing = 2 };
        durBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Duration"] ?? "Duration", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        durBlock.Children.Add(new TextBlock { Text = clip.FormattedDuration, FontSize = 13 });
        Grid.SetColumn(durBlock, 0);
        grid1.Children.Add(durBlock);

        var sizeBlock = new StackPanel { Spacing = 2 };
        sizeBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_FileSize"] ?? "File Size", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        sizeBlock.Children.Add(new TextBlock { Text = clip.FormattedFileSize, FontSize = 13 });
        Grid.SetColumn(sizeBlock, 1);
        grid1.Children.Add(sizeBlock);
        panel.Children.Add(grid1);

        // Grid for 2-column info (Resolution/FPS, Moments)
        var grid2 = new Grid { ColumnSpacing = 16 };
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resBlock = new StackPanel { Spacing = 2 };
        resBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Resolution"] ?? "Resolution", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        string resText = clip.VideoWidth > 0 ? $"{clip.VideoWidth} x {clip.VideoHeight}" + (clip.Framerate > 0 ? $" ({clip.Framerate:F0} fps)" : "") : (loc?["Dialog_ClipInfo_Unknown"] ?? "Unknown");
        resBlock.Children.Add(new TextBlock { Text = resText, FontSize = 13 });
        Grid.SetColumn(resBlock, 0);
        grid2.Children.Add(resBlock);

        var momentBlock = new StackPanel { Spacing = 2 };
        momentBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Moments"] ?? "Saved Moments", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        string momentsText = clip.Bookmarks.Count > 0 
            ? string.Format(loc?["Dialog_ClipInfo_MomentsMarked"] ?? "{0} marked", clip.Bookmarks.Count)
            : (loc?["Dialog_ClipInfo_None"] ?? "None");
        momentBlock.Children.Add(new TextBlock { Text = momentsText, FontSize = 13 });
        Grid.SetColumn(momentBlock, 1);
        grid2.Children.Add(momentBlock);
        panel.Children.Add(grid2);

        // Date modified
        var dateBlock = new StackPanel { Spacing = 2 };
        dateBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_DateModified"] ?? "Date Modified", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        dateBlock.Children.Add(new TextBlock { Text = clip.FormattedDate, FontSize = 13 });
        panel.Children.Add(dateBlock);

        // File path
        var pathBlock = new StackPanel { Spacing = 2 };
        pathBlock.Children.Add(new TextBlock { Text = loc?["Dialog_ClipInfo_Location"] ?? "Location", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        pathBlock.Children.Add(new TextBlock { Text = clip.FilePath, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"], TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        panel.Children.Add(pathBlock);

        var dialog = new ContentDialog
        {
            Title = loc?["Dialog_ClipInfo_Title"] ?? "Clip Information",
            Content = panel,
            CloseButtonText = loc?["Dialog_ClipInfo_Close"] ?? "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task ShowRenameDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var textBox = new TextBox
        {
            Text = System.IO.Path.GetFileNameWithoutExtension(clip.FileName)
        };
        textBox.Loaded += (s, e) => textBox.SelectAll();

        var dialog = new ContentDialog
        {
            Title = loc?["Dialog_Rename_Title"] ?? "Rename Clip",
            Content = textBox,
            PrimaryButtonText = loc?["Dialog_Rename_Confirm"] ?? "Rename",
            CloseButtonText = loc?["Dialog_Rename_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            ViewModel.RenameClipInternal(clip, textBox.Text.Trim());
        }
    }

    private async System.Threading.Tasks.Task ShowDeleteConfirmDialogAsync(GameClip clip)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        string message = string.Format(loc?["Dialog_Delete_Message"] ?? "Are you sure you want to move '{0}' to the Windows Recycle Bin?", clip.FileName);
        var dialog = new ContentDialog
        {
            Title = loc?["Dialog_Delete_Title"] ?? "Delete to Recycle Bin?",
            Content = message,
            PrimaryButtonText = loc?["Dialog_Delete_Confirm"] ?? "Delete",
            CloseButtonText = loc?["Dialog_Delete_Cancel"] ?? "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.DeleteClip(clip);
        }
    }
}
