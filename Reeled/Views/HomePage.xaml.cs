using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        MainSplitView.Loaded += (s, e) =>
        {
            AdjustSplitViewAnimationSpeed();
            UpdateActiveIndicator(animate: false);
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(HomeViewModel.CurrentSection)
                or nameof(HomeViewModel.IsHomeSelected)
                or nameof(HomeViewModel.IsFavoritesSelected)
                or nameof(HomeViewModel.IsSavedMomentsSelected)
                or nameof(HomeViewModel.IsFolderSelected))
            {
                UpdateActiveIndicator(animate: true);
            }
        };
    }

    private double _currentIndicatorY = 11;
    private double _targetIndicatorY = 11;
    private bool _isIndicatorVisible = true;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _indicatorStoryboard;

    private void UpdateActiveIndicator(bool animate = true)
    {
        if (ActiveIndicatorPill == null || IndicatorTranslation == null || IndicatorScale == null)
            return;

        if (ViewModel.IsHomeSelected)
        {
            double targetY = GetTargetIndicatorY(HomeNavButton, 11);
            AnimateIndicatorTo(targetY, animate);
        }
        else if (ViewModel.IsFavoritesSelected)
        {
            double targetY = GetTargetIndicatorY(FavoritesNavButton, 52);
            AnimateIndicatorTo(targetY, animate);
        }
        else if (ViewModel.IsSavedMomentsSelected)
        {
            double targetY = GetTargetIndicatorY(SavedMomentsNavButton, 93);
            AnimateIndicatorTo(targetY, animate);
        }
        else
        {
            // When a folder or nothing in the top section is selected
            AnimateIndicatorVisibility(false, animate);
        }
    }

    private double GetTargetIndicatorY(Button targetButton, double fallbackY)
    {
        try
        {
            if (targetButton != null && TopNavContainer != null && targetButton.ActualHeight > 0 && TopNavContainer.ActualHeight > 0)
            {
                var transform = targetButton.TransformToVisual(TopNavContainer);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                double pillHeight = ActiveIndicatorPill.ActualHeight > 0 ? ActiveIndicatorPill.ActualHeight : 16.0;
                return point.Y + (targetButton.ActualHeight - pillHeight) / 2.0;
            }
        }
        catch { }
        return fallbackY;
    }

    private void AnimateIndicatorTo(double targetY, bool animate)
    {
        if (!_isIndicatorVisible)
        {
            _indicatorStoryboard?.Stop();
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
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = ease
            };
            var animScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = ease
            };

            sb.Children.Add(animOpacity);
            sb.Children.Add(animScaleY);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, ActiveIndicatorPill);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleY, IndicatorScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleY, "ScaleY");

            _indicatorStoryboard = sb;
            sb.Begin();
            _isIndicatorVisible = true;
            return;
        }

        if (Math.Abs(_targetIndicatorY - targetY) < 1.0 && _isIndicatorVisible)
        {
            return;
        }

        _indicatorStoryboard?.Stop();
        double fromY = IndicatorTranslation.Y;
        _targetIndicatorY = targetY;

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
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        // 1. Vertical Glide Animation (Y translation)
        var animTranslate = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromY,
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = easeOut
        };
        moveSb.Children.Add(animTranslate);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animTranslate, IndicatorTranslation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animTranslate, "Y");

        // 2. Windows 11 Signature Fluid Stretch-and-Snap Animation
        if (distance > 5.0)
        {
            double stretch = Math.Min(1.45, 1.0 + (distance / 120.0));
            var animScaleKeyFrames = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)
            });
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                Value = stretch,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100)),
                EasingFunction = easeOut
            });
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                EasingFunction = easeOut
            });

            moveSb.Children.Add(animScaleKeyFrames);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animScaleKeyFrames, IndicatorScale);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animScaleKeyFrames, "ScaleY");
        }

        _currentIndicatorY = targetY;
        _indicatorStoryboard = moveSb;
        moveSb.Begin();
    }

    private void AnimateIndicatorVisibility(bool isVisible, bool animate)
    {
        if (_isIndicatorVisible == isVisible) return;

        _indicatorStoryboard?.Stop();

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

        _indicatorStoryboard = sb;
        sb.Begin();
        _isIndicatorVisible = isVisible;
    }

    private void AdjustSplitViewAnimationSpeed()
    {
        try
        {
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(MainSplitView) == 0) return;
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(MainSplitView, 0) is FrameworkElement root)
            {
                var groups = VisualStateManager.GetVisualStateGroups(root);
                foreach (var group in groups)
                {
                    if (group.Name == "DisplayModeStates")
                    {
                        foreach (var transition in group.Transitions)
                        {
                            if (transition.Storyboard != null)
                            {
                                AdjustStoryboardDuration(transition.Storyboard, TimeSpan.FromMilliseconds(300));
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void AdjustStoryboardDuration(Microsoft.UI.Xaml.Media.Animation.Storyboard sb, TimeSpan targetDuration)
    {
        foreach (var child in sb.Children)
        {
            if (child is Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames dak)
            {
                foreach (var kf in dak.KeyFrames)
                {
                    if (kf.KeyTime.TimeSpan > TimeSpan.Zero)
                    {
                        kf.KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(targetDuration);
                    }
                }
            }
            else if (child is Microsoft.UI.Xaml.Media.Animation.ObjectAnimationUsingKeyFrames oak)
            {
                foreach (var kf in oak.KeyFrames)
                {
                    if (kf.KeyTime.TimeSpan > TimeSpan.Zero)
                    {
                        kf.KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(targetDuration);
                    }
                }
            }
        }
    }

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSidebar();
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
                if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorSecondaryBrush", out var hoverCardBg))
                {
                    grid.Background = (Microsoft.UI.Xaml.Media.Brush)hoverCardBg;
                }
            }
            else
            {
                if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultStroke))
                {
                    grid.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)defaultStroke;
                }
                if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var defaultCardBg))
                {
                    grid.Background = (Microsoft.UI.Xaml.Media.Brush)defaultCardBg;
                }
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

    private void OnDirectoryExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is DirectoryNode dirNode)
        {
            dirNode.IsExpanded = true;
        }
        else if (args.Node?.Content is DirectoryNode dn)
        {
            dn.IsExpanded = true;
        }
    }

    private void OnDirectoryCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is DirectoryNode dirNode)
        {
            dirNode.IsExpanded = false;
        }
        else if (args.Node?.Content is DirectoryNode dn)
        {
            dn.IsExpanded = false;
        }
    }

    private void OnFolderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement el)
        {
            AnimateElementClickPulse(el, 0.96);
        }
    }

    private void OnFolderPointerReset(object sender, PointerRoutedEventArgs e)
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
            await ViewModel.SelectDirectoryAsync(targetNode);
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
            ViewModel.SelectedClip = clip;
            AnimateCardHover(element, isHovered: true);

            var flyout = new MenuFlyout();
            flyout.Closed += (s, args) =>
            {
                AnimateCardHover(element, isHovered: false);
            };

            var playItem = new MenuFlyoutItem { Text = "Play", Icon = new FontIcon { Glyph = "\uE768" } };
            playItem.Click += (s, args) => ViewModel.PlayClip(clip);
            flyout.Items.Add(playItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var explorerItem = new MenuFlyoutItem { Text = "Reveal in File Explorer", Icon = new FontIcon { Glyph = "\uEC50" } };
            explorerItem.Click += (s, args) => ViewModel.OpenInExplorer(clip);
            flyout.Items.Add(explorerItem);

            var copyItem = new MenuFlyoutItem { Text = "Copy File Path", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += (s, args) => ViewModel.CopyPath(clip);
            flyout.Items.Add(copyItem);

            var renameItem = new MenuFlyoutItem { Text = "Rename", Icon = new FontIcon { Glyph = "\uE8AC" } };
            renameItem.Click += async (s, args) => await ShowRenameDialogAsync(clip);
            flyout.Items.Add(renameItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var favItem = new MenuFlyoutItem
            {
                Text = clip.IsFavorite ? "Remove from Favorites" : "Add to Favorites",
                Icon = new FontIcon { Glyph = clip.IsFavorite ? "\uEB51" : "\uEB52" }
            };
            favItem.Click += (s, args) => _ = ViewModel.ToggleFavoriteAsync(clip);
            flyout.Items.Add(favItem);

            var deleteItem = new MenuFlyoutItem { Text = "Delete to Recycle Bin", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += async (s, args) => await ShowDeleteConfirmDialogAsync(clip);
            flyout.Items.Add(deleteItem);

            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task ShowRenameDialogAsync(GameClip clip)
    {
        var textBox = new TextBox
        {
            Text = System.IO.Path.GetFileNameWithoutExtension(clip.FileName)
        };
        textBox.Loaded += (s, e) => textBox.SelectAll();

        var dialog = new ContentDialog
        {
            Title = "Rename Clip",
            Content = textBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
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
        var dialog = new ContentDialog
        {
            Title = "Delete to Recycle Bin?",
            Content = $"Are you sure you want to move '{clip.FileName}' to the Windows Recycle Bin?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
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
