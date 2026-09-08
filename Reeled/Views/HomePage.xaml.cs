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

    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _sidebarStoryboard;

    public HomePage()
    {
        ViewModel = App.GetService<HomeViewModel>();
        InitializeComponent();

        SidebarBorder.SizeChanged += (s, e) =>
        {
            SidebarBorder.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height))
            };
        };
    }

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSidebar();
        AnimateSidebar(ViewModel.IsSidebarCollapsed);
    }

    private void AnimateSidebar(bool collapse)
    {
        _sidebarStoryboard?.Stop();

        SidebarBorder.Visibility = Visibility.Visible;

        double currentWidth = SidebarBorder.ActualWidth > 0 ? SidebarBorder.ActualWidth : (collapse ? 290 : 0);
        double targetWidth = collapse ? 0 : 290;

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
            { 
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut 
            }
        };

        _sidebarStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        _sidebarStoryboard.Children.Add(animation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, SidebarBorder);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Width");

        _sidebarStoryboard.Completed += (s, e) =>
        {
            SidebarBorder.Width = targetWidth;
            if (collapse)
            {
                SidebarBorder.Visibility = Visibility.Collapsed;
            }
        };

        _sidebarStoryboard.Begin();
    }

    private void OnClipCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid card)
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentBrush))
            {
                card.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)accentBrush;
            }
        }
    }

    private void OnClipCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid card)
        {
            if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultStroke))
            {
                card.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)defaultStroke;
            }
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

            var flyout = new MenuFlyout();

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
