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
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.CurrentSection))
            {
                UpdateActiveIndicator(animate: true);
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
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        // 1. Vertical Glide Animation (Y translation)
        var animTranslate = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromY,
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = easeOut
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
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)),
                EasingFunction = easeOut
            });
            animScaleKeyFrames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                Value = 1.0,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)),
                EasingFunction = easeOut
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
        var ease = new CubicEase
        {
            EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseInOut
        };
        var duration = TimeSpan.FromMilliseconds(isOpen ? 280 : 250);

        // 1. Width animation on container with EnableDependentAnimation = true
        // Crucial: WinUI 3 requires EnableDependentAnimation for FrameworkElement.Width.
        // This ensures the layout pipeline updates SidebarColumn (Auto) and MainContentColumn (*) every frame,
        // delivering a responsive "display flex" resize for the clip viewer on the right.
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

        // 3. Opacity animation
        var animOpacity = new DoubleAnimation
        {
            From = startOpacity,
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 180),
            EasingFunction = ease
        };
        if (!isOpen)
        {
            animOpacity.BeginTime = TimeSpan.FromMilliseconds(50);
        }
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

    private readonly System.Collections.Generic.HashSet<DirectoryNode> _animatingExpandingParents = new();
    private readonly System.Collections.Generic.List<Storyboard> _activeFolderStoryboards = new();

    private void OnDirectoryExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode != null)
        {
            dirNode.IsExpanded = true;
            _animatingExpandingParents.Add(dirNode);

            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1200);
                _animatingExpandingParents.Remove(dirNode);
            });
        }
    }

    private void OnDirectoryCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        var dirNode = args.Item as DirectoryNode ?? (args.Node?.Content as DirectoryNode);
        if (dirNode != null)
        {
            dirNode.IsExpanded = false;
            _animatingExpandingParents.Remove(dirNode);
        }
    }

    private void OnFolderItemGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement grid && grid.DataContext is DirectoryNode childNode)
        {
            // Always guarantee full visibility and zero translate offset
            grid.Opacity = 1.0;
            if (grid.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform tt)
            {
                tt.Y = 0.0;
            }

            DirectoryNode? parent = null;
            foreach (var p in _animatingExpandingParents)
            {
                if (p.SubDirectories.Contains(childNode))
                {
                    parent = p;
                    break;
                }
            }

            if (parent != null)
            {
                int index = parent.SubDirectories.IndexOf(childNode);
                if (index >= 0)
                {
                    AnimateCascadeEntrance(grid, index);
                }
            }
        }
    }

    private void AnimateCascadeEntrance(FrameworkElement element, int index)
    {
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

        // Fluid Windows 11 cascade: items slide smoothly down from parent folder
        double slideDistance = -(14.0 + Math.Min(index * 1.5, 10.0));
        trans.Y = slideDistance;

        // NOTE: We do not set element.Opacity = 0.0 here!
        // Instead, the DoubleAnimation animates From = 0.0 To = 1.0.
        // If the animation is ever interrupted, recycled, or finished, the base opacity remains 1.0.
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 20);
        var duration = TimeSpan.FromMilliseconds(220);
        var easeOut = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = slideDistance,
            To = 0.0,
            BeginTime = delay,
            Duration = duration,
            EasingFunction = easeOut
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(TranslateTransform.Y)");

        var animOp = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            BeginTime = delay,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = easeOut
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOp, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOp, "Opacity");

        sb.Children.Add(animY);
        sb.Children.Add(animOp);

        _activeFolderStoryboards.Add(sb);

        sb.Completed += (s, e) =>
        {
            _activeFolderStoryboards.Remove(sb);
            trans.Y = 0.0;
            element.Opacity = 1.0;
        };

        sb.Begin();
    }

    private void OnFolderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el)
        {
            // Guard against clicks in the far top and bottom container edges
            var pt = e.GetCurrentPoint(el).Position;
            if (pt.Y < 3.0 || pt.Y > el.ActualHeight - 3.0)
            {
                return;
            }
            AnimateElementClickPulse(el, 0.98);
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

            // Section 1: Playback
            var playItem = new MenuFlyoutItem { Text = "Play", Icon = new FontIcon { Glyph = "\uE768" } };
            playItem.Click += (s, args) => ViewModel.PlayClip(clip);
            flyout.Items.Add(playItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 2: Clip Info & Management
            var infoItem = new MenuFlyoutItem { Text = "Clip Information", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += async (s, args) => await ShowClipInfoDialogAsync(clip);
            flyout.Items.Add(infoItem);

            var favItem = new MenuFlyoutItem
            {
                Text = clip.IsFavorite ? "Remove from Favorites" : "Add to Favorites",
                Icon = new FontIcon { Glyph = clip.IsFavorite ? "\uEB51" : "\uEB52" }
            };
            favItem.Click += (s, args) => _ = ViewModel.ToggleFavoriteAsync(clip);
            flyout.Items.Add(favItem);

            var renameItem = new MenuFlyoutItem { Text = "Rename", Icon = new FontIcon { Glyph = "\uE8AC" } };
            renameItem.Click += async (s, args) => await ShowRenameDialogAsync(clip);
            flyout.Items.Add(renameItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 3: File Location & System
            var explorerItem = new MenuFlyoutItem { Text = "Reveal in File Explorer", Icon = new FontIcon { Glyph = "\uEC50" } };
            explorerItem.Click += (s, args) => ViewModel.OpenInExplorer(clip);
            flyout.Items.Add(explorerItem);

            var copyItem = new MenuFlyoutItem { Text = "Copy File Path", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += (s, args) => ViewModel.CopyPath(clip);
            flyout.Items.Add(copyItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Section 4: Destructive
            var deleteItem = new MenuFlyoutItem { Text = "Delete to Recycle Bin", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += async (s, args) => await ShowDeleteConfirmDialogAsync(clip);
            flyout.Items.Add(deleteItem);

            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task ShowClipInfoDialogAsync(GameClip clip)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 340, MaxWidth = 440 };

        // File name
        var nameBlock = new StackPanel { Spacing = 2 };
        nameBlock.Children.Add(new TextBlock { Text = "File Name", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        nameBlock.Children.Add(new TextBlock { Text = clip.FileName, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(nameBlock);

        // Grid for 2-column info (Duration, File Size)
        var grid1 = new Grid { ColumnSpacing = 16 };
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var durBlock = new StackPanel { Spacing = 2 };
        durBlock.Children.Add(new TextBlock { Text = "Duration", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        durBlock.Children.Add(new TextBlock { Text = clip.FormattedDuration, FontSize = 13 });
        Grid.SetColumn(durBlock, 0);
        grid1.Children.Add(durBlock);

        var sizeBlock = new StackPanel { Spacing = 2 };
        sizeBlock.Children.Add(new TextBlock { Text = "File Size", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        sizeBlock.Children.Add(new TextBlock { Text = clip.FormattedFileSize, FontSize = 13 });
        Grid.SetColumn(sizeBlock, 1);
        grid1.Children.Add(sizeBlock);
        panel.Children.Add(grid1);

        // Grid for 2-column info (Resolution/FPS, Moments)
        var grid2 = new Grid { ColumnSpacing = 16 };
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resBlock = new StackPanel { Spacing = 2 };
        resBlock.Children.Add(new TextBlock { Text = "Resolution", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        string resText = clip.VideoWidth > 0 ? $"{clip.VideoWidth} x {clip.VideoHeight}" + (clip.Framerate > 0 ? $" ({clip.Framerate:F0} fps)" : "") : "Unknown";
        resBlock.Children.Add(new TextBlock { Text = resText, FontSize = 13 });
        Grid.SetColumn(resBlock, 0);
        grid2.Children.Add(resBlock);

        var momentBlock = new StackPanel { Spacing = 2 };
        momentBlock.Children.Add(new TextBlock { Text = "Saved Moments", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        momentBlock.Children.Add(new TextBlock { Text = clip.Bookmarks.Count > 0 ? $"{clip.Bookmarks.Count} marked" : "None", FontSize = 13 });
        Grid.SetColumn(momentBlock, 1);
        grid2.Children.Add(momentBlock);
        panel.Children.Add(grid2);

        // Date modified
        var dateBlock = new StackPanel { Spacing = 2 };
        dateBlock.Children.Add(new TextBlock { Text = "Date Modified", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        dateBlock.Children.Add(new TextBlock { Text = clip.FormattedDate, FontSize = 13 });
        panel.Children.Add(dateBlock);

        // File path
        var pathBlock = new StackPanel { Spacing = 2 };
        pathBlock.Children.Add(new TextBlock { Text = "Location", FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        pathBlock.Children.Add(new TextBlock { Text = clip.FilePath, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"], TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        panel.Children.Add(pathBlock);

        var dialog = new ContentDialog
        {
            Title = "Clip Information",
            Content = panel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
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
