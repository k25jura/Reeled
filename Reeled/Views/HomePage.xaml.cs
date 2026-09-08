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
        // 1. Unified Outline & Background Highlight
        if (card is Grid grid)
        {
            var isSelected = grid.DataContext is GameClip clip && clip == ViewModel.SelectedClip;
            if (isHovered || isSelected)
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

    private async void OnDirectoryTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is DirectoryNode dirNode)
        {
            await ViewModel.SelectDirectoryAsync(dirNode);
        }
        else if (args.InvokedItem is TreeViewItem tvi && tvi.DataContext is DirectoryNode dn)
        {
            await ViewModel.SelectDirectoryAsync(dn);
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
                Icon = new FontIcon { Glyph = clip.IsFavorite ? "\uE734" : "\uE735" }
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
