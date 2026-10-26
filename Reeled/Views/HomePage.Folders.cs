using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Reeled.Helpers;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.Views;

/// <summary>
/// Folders from the library: New folder, Rename (F2 on a folder in the tree) and moving a folder to the
/// Recycle Bin. These are in the folder menus of the sidebar, the bar's chips and the clip view. Removing a
/// watch folder from the library is a separate command and leaves the folder on disk.
/// </summary>
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

    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    /// <summary>Paste here, New folder, Rename and Move to Recycle Bin for a folder (after a separator).</summary>
    private void AddFolderFileMenuItems(IList<MenuFlyoutItemBase> items, DirectoryNode node)
    {
        var loc = LayoutLoc;
        void Item(string key, string glyph, Action click, bool enabled = true, string? accelerator = null)
        {
            var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            if (accelerator != null) item.KeyboardAcceleratorTextOverride = accelerator;
            item.Click += (s, args) => click();
            items.Add(item);
        }

        if (items.Count > 0 && items[^1] is not MenuFlyoutSeparator) items.Add(new MenuFlyoutSeparator());
        Item("FolderMenu_Paste", "", () => _ = PasteAsync(node.FullPath), ClipboardHasFiles());
        Item("FolderMenu_NewFolder", "", () => _ = NewFolderInAsync(node.FullPath), accelerator: "Ctrl+Shift+N");
        Item("FolderMenu_Rename", "", () => _ = RenameFolderAsync(node), accelerator: "F2");
        Item("FolderMenu_Delete", "", () => _ = DeleteFolderAsync(node), accelerator: "Del");
    }

    /// <summary>The context menu of a folder in the bar's dropdowns: open in a new tab, plus the file commands.</summary>
    private MenuFlyout CreateFolderMenu(DirectoryNode node)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateOpenInNewTabItem(node));
        AddFolderFileMenuItems(menu.Items, node);
        return menu;
    }

    // ---------------------------------------------------------------------------------------------
    // Name dialog shared by New folder and Rename
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reason a folder name can't be used, or null when it is fine.</summary>
    private string? NameProblem(string name)
    {
        var loc = LayoutLoc;
        return App.GetService<IFileOperationService>().ValidateName(name) switch
        {
            FileNameProblem.Empty => loc["Folder_Error_Empty"],
            FileNameProblem.InvalidCharacters => loc["Dialog_Rename_InvalidChars"],
            FileNameProblem.Reserved => loc["Folder_Error_Name"],
            _ => null
        };
    }

    /// <summary>
    /// Asks for a name. <paramref name="apply"/> does the work while the dialog is still open and returns what is
    /// wrong (shown under the box, the dialog stays) or null when it worked. Returns the name that was applied.
    /// </summary>
    private async Task<string?> PromptNameAsync(string title, string initial, string confirmText, Func<string, Task<string?>> apply)
    {
        var loc = LayoutLoc;
        var textBox = new TextBox { Text = initial, RequestedTheme = DialogTheme, MaxLength = 200 };
        textBox.Loaded += (s, e) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrushes.Get("SystemFillColorCriticalBrush", this),
            Visibility = Visibility.Collapsed
        };
        textBox.TextChanged += (s, e) => error.Visibility = Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 8, MinWidth = 340, RequestedTheme = DialogTheme };
        panel.Children.Add(textBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = DialogTheme,
            Title = title,
            Content = panel,
            PrimaryButtonText = confirmText,
            CloseButtonText = loc["Common_Cancel"],
            DefaultButton = ContentDialogButton.Primary
        };

        string? applied = null;
        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                string name = textBox.Text.Trim();
                string? problem = await apply(name);
                if (problem != null)
                {
                    error.Text = problem;
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
                else
                {
                    applied = name;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Folder name dialog", ex);
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        try
        {
            await dialog.ShowSafeAsync();
        }
        catch (Exception ex)
        {
            // Another dialog is open
            Log.Error("Folder name dialog", ex);
        }
        return applied;
    }

    private string DescribeFolderError(Exception ex) => ex switch
    {
        NameTakenException => LayoutLoc["Folder_Error_Exists"],
        UnauthorizedAccessException => LayoutLoc["FileOps_Error_Access"],
        _ => ex.Message
    };

    /// <summary>The bar's folder chips show a dropdown arrow only for folders that have subfolders.</summary>
    private void RefreshNavAfterFolderChange()
    {
        if (IsHorizontalNav) BuildHorizontalNav(snapIndicator: false);
        SelectTreeFolderFor(ViewModel.ActiveTab);
        UpdateActiveIndicator(animate: true);
        QueueHorizontalIndicator(animate: false);
    }

    // ---------------------------------------------------------------------------------------------
    // New folder
    // ---------------------------------------------------------------------------------------------

    private async Task NewFolderFromShortcutAsync()
    {
        string? parent = (TryGetFocusedTreeFolder(out var focused) ? focused.FullPath : null) ?? ViewModel.CurrentFolderPath;
        if (parent == null)
        {
            ShowFileOpMessage(LayoutLoc["Folder_NeedParent"], error: true);
            return;
        }
        await NewFolderInAsync(parent);
    }

    private async Task NewFolderInAsync(string parentPath)
    {
        var loc = LayoutLoc;
        DirectoryNode? created = null;
        string? name = await PromptNameAsync(loc["Dialog_NewFolder_Title"], loc["Dialog_NewFolder_Default"], loc["Dialog_NewFolder_Confirm"], candidate =>
        {
            string? problem = NameProblem(candidate);
            if (problem != null) return Task.FromResult<string?>(problem);
            try
            {
                created = ViewModel.CreateFolder(parentPath, candidate);
                return Task.FromResult<string?>(null);
            }
            catch (Exception ex)
            {
                return Task.FromResult<string?>(DescribeFolderError(ex));
            }
        });
        if (name == null || created == null) return;

        // Show it: open the folder it was made in
        var parent = Reeled.ViewModels.HomeViewModel.FindNodeByPath(ViewModel.Directories, parentPath);
        if (parent != null && !parent.IsExpanded) ExpandDirectoryWithAnimation(parent);
        RefreshNavAfterFolderChange();
        ShowFileOpMessage(string.Format(loc["Folder_Created"], name), autoHideMs: 2200, glyph: "");
    }

    // ---------------------------------------------------------------------------------------------
    // Rename
    // ---------------------------------------------------------------------------------------------

    private async Task RenameFolderAsync(DirectoryNode node)
    {
        var loc = LayoutLoc;
        string current = node.DisplayName;
        string? name = await PromptNameAsync(loc["Dialog_RenameFolder_Title"], current, loc["Dialog_Rename_Confirm"], async candidate =>
        {
            string? problem = NameProblem(candidate);
            if (problem != null) return problem;
            if (string.Equals(candidate, current, StringComparison.Ordinal)) return null;
            try
            {
                await ViewModel.RenameFolderAsync(node, candidate);
                return null;
            }
            catch (Exception ex)
            {
                return DescribeFolderError(ex);
            }
        });
        if (name == null) return;
        RefreshNavAfterFolderChange();
    }

    // ---------------------------------------------------------------------------------------------
    // Delete (to the Recycle Bin)
    // ---------------------------------------------------------------------------------------------

    private async Task DeleteFolderAsync(DirectoryNode node)
    {
        var loc = LayoutLoc;
        int clips = node.ClipCount;
        string message = clips > 0
            ? string.Format(loc["Dialog_DeleteFolder_Message"], node.DisplayName, clips, loc.FormatPlural("Plural_Clip", clips))
            : string.Format(loc["Dialog_DeleteFolder_MessageEmpty"], node.DisplayName);
        if (node.IsWatchRoot) message += "\n\n" + loc["Dialog_DeleteFolder_Root"];

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = DialogTheme,
            Title = loc["Dialog_DeleteFolder_Title"],
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = loc["Dialog_Delete_Confirm"],
            CloseButtonText = loc["Dialog_Delete_Cancel"],
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult answer;
        try
        {
            answer = await dialog.ShowSafeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Delete folder dialog", ex);
            return;
        }
        if (answer != ContentDialogResult.Primary) return;

        bool removed = await ViewModel.DeleteFolderAsync(node);
        RefreshNavAfterFolderChange();
        if (!removed) await ShowErrorDialogAsync(loc["Dialog_DeleteFolder_Failed"]);
    }
}
