using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>Creating, renaming and deleting folders from the library.</summary>
public sealed partial class HomePage
{
    /// <summary>The folder selected in the tree, when the keyboard focus is in the tree.</summary>
    private bool TryGetFocusedTreeFolder(out DirectoryNode node)
    {
        node = null!;
        if (XamlRoot == null || DirectoriesTreeView?.SelectedItem is not DirectoryNode selected) return false;
        var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, DirectoriesTreeView))
            {
                node = selected;
                return true;
            }
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private Task RenameFolderAsync(DirectoryNode node) => Task.CompletedTask;

    private Task DeleteFolderAsync(DirectoryNode node) => Task.CompletedTask;
}
