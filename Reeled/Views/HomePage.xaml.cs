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
        InitializeComponent();
        ViewModel = App.GetService<HomeViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.SyncDirectoriesAsync();
        RefreshTreeNodes();
    }

    private void RefreshTreeNodes()
    {
        DirectoriesTreeView.RootNodes.Clear();
        foreach (var dir in ViewModel.Directories)
        {
            var rootNode = CreateTreeNode(dir);
            DirectoriesTreeView.RootNodes.Add(rootNode);
        }

        if (ViewModel.SelectedDirectory != null)
        {
            SelectNodeInTree(DirectoriesTreeView.RootNodes, ViewModel.SelectedDirectory.FullPath);
        }
    }

    private TreeViewNode CreateTreeNode(DirectoryNode dir)
    {
        var node = new TreeViewNode
        {
            Content = dir,
            IsExpanded = true
        };
        foreach (var sub in dir.SubDirectories)
        {
            node.Children.Add(CreateTreeNode(sub));
        }
        return node;
    }

    private bool SelectNodeInTree(System.Collections.Generic.IList<TreeViewNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (n.Content is DirectoryNode d && string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                DirectoriesTreeView.SelectedNode = n;
                return true;
            }
            if (SelectNodeInTree(n.Children, path))
                return true;
        }
        return false;
    }

    private async void OnDirectoryTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode tvNode && tvNode.Content is DirectoryNode dirNode)
        {
            await ViewModel.SelectDirectoryAsync(dirNode);
        }
        else if (args.InvokedItem is DirectoryNode dNode)
        {
            await ViewModel.SelectDirectoryAsync(dNode);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddDirectoryAsync(App.WindowHandle);
        RefreshTreeNodes();
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
