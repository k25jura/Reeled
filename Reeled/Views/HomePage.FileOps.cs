using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Reeled.Controls;
using Reeled.Helpers;
using Reeled.Models;
using Reeled.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Reeled.Views;

/// <summary>
/// File management in the library: the clipboard (Ctrl+C / X / V with real files, so pasting works in
/// Explorer and Discord and files copied in Explorer paste here), deleting several clips at once, and the
/// card that shows copy and move progress with conflicts resolved through dialogs.
/// </summary>
public sealed partial class HomePage
{
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private long _ownClipboardUntil;
    private FileOperationCard? _opCard;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _opCardTimer;
    private bool _fileOpBusy;
    private bool _conflictsRepeat;


    // ---------------------------------------------------------------------------------------------
    // Clipboard
    // ---------------------------------------------------------------------------------------------

    private void WireClipboard()
    {
        try
        {
            // Whatever lands on the clipboard after a cut takes the cut away (the dimmed clips come back)
            Clipboard.ContentChanged += (s, e) =>
            {
                if (_cutPaths.Count == 0 || Environment.TickCount64 < _ownClipboardUntil) return;
                DispatcherQueue.TryEnqueue(ClearCutMarks);
            };
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard.ContentChanged", ex);
        }
    }

    private void ClearCutMarks()
    {
        if (_cutPaths.Count == 0) return;
        _cutPaths.Clear();
        foreach (var clip in ViewModel.AllClips)
        {
            if (clip.IsCut) clip.IsCut = false;
        }
    }

    /// <summary>The library reloads its clips now and then; the cut ones stay dimmed.</summary>
    private void ReapplyCutMarks()
    {
        if (_cutPaths.Count == 0) return;
        foreach (var clip in ViewModel.AllClips) clip.IsCut = _cutPaths.Contains(clip.FilePath);
    }

    private async Task CopyClipsToClipboardAsync(IReadOnlyList<GameClip> clips, bool cut)
    {
        if (clips.Count == 0) return;
        var loc = LayoutLoc;

        var files = new List<IStorageItem>();
        foreach (var clip in clips)
        {
            try
            {
                files.Add(await StorageFile.GetFileFromPathAsync(clip.FilePath));
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard file", ex);
            }
        }
        if (files.Count == 0)
        {
            ShowFileOpMessage(loc["FileOps_ClipboardFailed"], error: true);
            return;
        }

        var package = new DataPackage { RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy };
        package.SetStorageItems(files);
        ClearCutMarks();
        try
        {
            _ownClipboardUntil = Environment.TickCount64 + 1500;
            Clipboard.SetContent(package);
            // Keeps the files on the clipboard for other apps even if Reeled is closed
            try { Clipboard.Flush(); } catch { }
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard.SetContent", ex);
            ShowFileOpMessage(loc["FileOps_ClipboardFailed"], error: true);
            return;
        }

        if (cut)
        {
            foreach (var clip in clips)
            {
                clip.IsCut = true;
                _cutPaths.Add(clip.FilePath);
            }
        }
        string what = DescribeCount(clips.Count, clips[0].FileName);
        ShowFileOpMessage(string.Format(loc[cut ? "FileOps_Cut" : "FileOps_Copied"], what), autoHideMs: 1800);
    }

    private static bool ClipboardHasFiles()
    {
        try
        {
            return Clipboard.GetContent().Contains(StandardDataFormats.StorageItems);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pastes the files on the clipboard into a folder (the open one by default).</summary>
    private async Task PasteAsync(string? destinationFolder = null)
    {
        var loc = LayoutLoc;
        destinationFolder ??= ViewModel.CurrentFolderPath;
        if (destinationFolder == null)
        {
            ShowFileOpMessage(loc["FileOps_NeedFolder"], error: true);
            return;
        }

        DataPackageView view;
        IReadOnlyList<IStorageItem> items;
        try
        {
            view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.StorageItems)) return;
            items = await view.GetStorageItemsAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard paste", ex);
            ShowFileOpMessage(loc["FileOps_ClipboardFailed"], error: true);
            return;
        }

        var paths = items.OfType<StorageFile>()
            .Select(f => f.Path)
            .Where(p => !string.IsNullOrEmpty(p) && PathUtil.IsVideoFile(p))
            .ToList();
        if (paths.Count == 0)
        {
            ShowFileOpMessage(loc["FileOps_NoVideos"], error: true);
            return;
        }

        var operation = view.RequestedOperation;
        bool move = (operation & DataPackageOperation.Move) != 0 && (operation & DataPackageOperation.Copy) == 0
                    || paths.Any(_cutPaths.Contains);
        var result = await RunTransferAsync(paths, destinationFolder, move ? FileTransferKind.Move : FileTransferKind.Copy);

        if (move && result != null && result.Done.Count > 0)
        {
            // Like Explorer: a pasted cut is used up
            ClearCutMarks();
            try
            {
                _ownClipboardUntil = Environment.TickCount64 + 1500;
                Clipboard.Clear();
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Shortcuts: Ctrl+A, Esc, Ctrl+C / X / V, Ctrl+Shift+N
    // ---------------------------------------------------------------------------------------------

    private void RegisterFileOpShortcuts(Action<VirtualKey, VirtualKeyModifiers, Func<bool>> add)
    {
        add(VirtualKey.A, VirtualKeyModifiers.Control, () =>
        {
            if (IsTextInputFocused() || ViewModel.FilteredClips.Count == 0) return false;
            SelectAllClips();
            return true;
        });
        add(VirtualKey.Escape, VirtualKeyModifiers.None, () =>
        {
            if (!IsSelecting || IsTextInputFocused()) return false;
            ClearSelection();
            return true;
        });
        add(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            _ = NewFolderFromShortcutAsync();
            return true;
        });
        add(VirtualKey.C, VirtualKeyModifiers.Control, () => ClipboardShortcut(cut: false));
        add(VirtualKey.X, VirtualKeyModifiers.Control, () => ClipboardShortcut(cut: true));
        add(VirtualKey.V, VirtualKeyModifiers.Control, () =>
        {
            if (IsTextInputFocused() || !ClipboardHasFiles()) return false;
            _ = PasteAsync();
            return true;
        });
    }

    private bool ClipboardShortcut(bool cut)
    {
        if (IsTextInputFocused()) return false;
        var clips = ActionClips();
        if (clips.Count == 0) return false;
        _ = CopyClipsToClipboardAsync(clips, cut);
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // The empty part of the clip view: Paste, New folder, Select all
    // ---------------------------------------------------------------------------------------------

    private void OnClipsAreaRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (FindParent<GridViewItem>(source) != null || FindParent<ListViewItem>(source) != null || FindParent<Button>(source) != null))
        {
            return;
        }

        var loc = LayoutLoc;
        string? folder = ViewModel.CurrentFolderPath;
        var flyout = new MenuFlyout();
        if (XamlRoot != null) flyout.XamlRoot = XamlRoot;
        flyout.Opening += OnFlyoutOpening;
        flyout.Opened += OnFlyoutOpened;

        void Item(string key, string glyph, Action click, bool enabled = true, string? accelerator = null)
        {
            var item = new MenuFlyoutItem { Text = loc[key], Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            if (accelerator != null) item.KeyboardAcceleratorTextOverride = accelerator;
            item.Click += (s, args) => click();
            flyout.Items.Add(item);
        }

        Item("ContextMenu_Paste", "", () => _ = PasteAsync(), folder != null && ClipboardHasFiles(), "Ctrl+V");
        Item("FolderMenu_NewFolder", "", () => _ = NewFolderInAsync(folder!), folder != null, "Ctrl+Shift+N");
        flyout.Items.Add(new MenuFlyoutSeparator());
        Item("ContextMenu_SelectAll", "", SelectAllClips, ViewModel.FilteredClips.Count > 0, "Ctrl+A");

        flyout.ShowAt(ClipsHost, e.GetPosition(ClipsHost));
        e.Handled = true;
    }

    /// <summary>The right-click menu of a multi-selection: only what makes sense for several clips.</summary>
    private void AddMultiSelectionMenuItems(MenuFlyout flyout, IReadOnlyList<GameClip> clips)
    {
        var loc = LayoutLoc;
        void Item(string text, string glyph, Action click, string? accelerator = null)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            if (accelerator != null) item.KeyboardAcceleratorTextOverride = accelerator;
            item.Click += (s, args) => click();
            flyout.Items.Add(item);
        }

        Item(loc["ContextMenu_Cut"], "", () => _ = CopyClipsToClipboardAsync(clips, cut: true), "Ctrl+X");
        Item(loc["ContextMenu_Copy"], "", () => _ = CopyClipsToClipboardAsync(clips, cut: false), "Ctrl+C");
        flyout.Items.Add(new MenuFlyoutSeparator());

        bool allFavorite = clips.All(c => c.IsFavorite);
        Item(loc[allFavorite ? "ContextMenu_RemoveFromFavoritesMany" : "ContextMenu_AddToFavoritesMany"],
            allFavorite ? "" : "",
            () =>
            {
                foreach (var clip in clips.Where(c => c.IsFavorite == allFavorite)) _ = ViewModel.ToggleFavoriteAsync(clip);
            });
        Item(loc["Selection_Clear"], "", ClearSelection);
        flyout.Items.Add(new MenuFlyoutSeparator());
        Item(string.Format(loc["ContextMenu_DeleteMany"], clips.Count), "", () => _ = ShowDeleteConfirmDialogAsync(clips), "Del");
    }

    // ---------------------------------------------------------------------------------------------
    // Deleting clips (one or several, one confirmation)
    // ---------------------------------------------------------------------------------------------

    private async Task ShowDeleteConfirmDialogAsync(IReadOnlyList<GameClip> clips)
    {
        if (clips.Count == 0) return;
        var loc = LayoutLoc;
        string message = clips.Count == 1
            ? string.Format(loc["Dialog_Delete_Message"], clips[0].FileName)
            : string.Format(loc["Dialog_DeleteMany_Message"], clips.Count, loc.FormatPlural("Plural_Clip", clips.Count));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = DialogTheme,
            Title = loc["Dialog_Delete_Title"],
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = loc["Dialog_Delete_Confirm"],
            CloseButtonText = loc["Dialog_Delete_Cancel"],
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        int failed = await ViewModel.DeleteClipsAsync(clips);
        if (failed > 0)
        {
            await ShowErrorDialogAsync(clips.Count == 1
                ? loc["Dialog_Delete_Failed"]
                : string.Format(loc["Dialog_DeleteMany_Failed"], failed));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Copying and moving with progress, conflicts and errors
    // ---------------------------------------------------------------------------------------------

    private string FolderDisplayName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>"clip.mp4" for one file, "3 clips" for several.</summary>
    private string DescribeCount(int count, string? singleName) =>
        count == 1 && !string.IsNullOrEmpty(singleName) ? singleName : $"{count} {LayoutLoc.FormatPlural("Plural_Clip", count)}";

    private async Task<FileTransferResult?> RunTransferAsync(IReadOnlyList<string> paths, string destinationFolder, FileTransferKind kind)
    {
        var loc = LayoutLoc;
        if (_fileOpBusy)
        {
            ShowFileOpMessage(loc["FileOps_Busy"], error: true);
            return null;
        }

        _fileOpBusy = true;
        _conflictsRepeat = paths.Count > 1;
        var cts = new CancellationTokenSource();
        string folderName = FolderDisplayName(destinationFolder);
        bool copy = kind == FileTransferKind.Copy;

        var card = EnsureOpCard();
        card.ShowProgress(
            string.Format(loc[copy ? "FileOps_Copying" : "FileOps_Moving"], DescribeCount(paths.Count, Path.GetFileName(paths[0])), folderName),
            copy ? "" : "",
            loc["Common_Cancel"]);
        void RequestCancel()
        {
            cts.Cancel();
            card.SetCancelling(loc["FileOps_Cancelling"]);
        }
        card.CancelRequested += RequestCancel;
        _opCardTimer?.Stop();
        ShowFloating(card);

        bool finished = false;
        var progress = new Progress<FileTransferProgress>(p =>
        {
            if (finished || cts.IsCancellationRequested) return;
            string detail = p.ItemCount > 1
                ? $"{p.CurrentName}  ·  {string.Format(loc["FileOps_Progress"], Math.Min(p.ItemIndex + 1, p.ItemCount), p.ItemCount)}"
                : p.CurrentName;
            double fraction = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : (p.ItemCount > 0 ? (double)p.ItemIndex / p.ItemCount : 0);
            card.SetProgress(detail, fraction);
        });

        FileTransferResult result;
        try
        {
            result = await ViewModel.TransferClipsAsync(paths, destinationFolder, kind, ResolveConflictAsync, progress, cts.Token);
        }
        catch (Exception ex)
        {
            Log.Error("RunTransfer", ex);
            result = new FileTransferResult();
            foreach (var path in paths) result.Failed.Add((path, ex));
        }
        finally
        {
            finished = true;
            card.CancelRequested -= RequestCancel;
            _fileOpBusy = false;
            cts.Dispose();
        }

        // The result line: what was done, plus anything that was skipped or failed
        int done = result.Done.Count;
        string summary;
        if (done > 0)
        {
            string what = DescribeCount(done, Path.GetFileName(result.Done[0].Destination));
            summary = string.Format(loc[copy ? "FileOps_Copied_To" : "FileOps_Moved_To"], what, folderName);
        }
        else
        {
            summary = result.Cancelled ? loc["FileOps_CancelledNothing"] : loc["FileOps_NothingDone"];
        }
        if (result.Cancelled && done > 0) summary += "  ·  " + loc["FileOps_Cancelled"];
        if (result.Skipped > 0) summary += "  ·  " + string.Format(loc["FileOps_SkippedCount"], result.Skipped);
        if (result.Failed.Count > 0) summary += "  ·  " + string.Format(loc["FileOps_FailedCount"], result.Failed.Count);

        bool problem = result.Failed.Count > 0;
        if (done == 0 && result.Skipped == 0 && !problem && !result.Cancelled)
        {
            HideFloating(card);
        }
        else
        {
            ShowFileOpMessage(summary, error: problem, autoHideMs: problem ? 7000 : 3500);
        }

        if (problem) await ShowTransferErrorsAsync(result);
        return result;
    }

    private FileOperationCard EnsureOpCard()
    {
        if (_opCard != null) return _opCard;
        _opCard = new FileOperationCard { Visibility = Visibility.Collapsed, Opacity = 0 };
        // Above the selection bar
        _fileOpStack.Children.Insert(0, _opCard);
        return _opCard;
    }

    /// <summary>A short line on the floating card (result of an operation, or why something can't be done).</summary>
    private void ShowFileOpMessage(string message, bool error = false, int autoHideMs = 3500)
    {
        if (_fileOpBusy) return;
        var card = EnsureOpCard();
        card.ShowResult(message, error);
        ShowFloating(card);

        _opCardTimer ??= DispatcherQueue.CreateTimer();
        _opCardTimer.Stop();
        _opCardTimer.Interval = TimeSpan.FromMilliseconds(autoHideMs);
        _opCardTimer.IsRepeating = false;
        _opCardTimer.Tick -= OnOpCardTimerTick;
        _opCardTimer.Tick += OnOpCardTimerTick;
        _opCardTimer.Start();
    }

    private void OnOpCardTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_opCard != null && !_fileOpBusy) HideFloating(_opCard);
    }

    private async Task<ConflictDecision> ResolveConflictAsync(FileConflict conflict)
    {
        var loc = LayoutLoc;
        string name = Path.GetFileName(conflict.DestinationPath);
        string folder = FolderDisplayName(Path.GetDirectoryName(conflict.DestinationPath));

        var panel = new StackPanel { Spacing = 12, MinWidth = 340, MaxWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(loc["FileOps_Conflict_Message"], name, folder),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(DescribeFileForConflict(loc["FileOps_Conflict_Incoming"], conflict.SourcePath));
        panel.Children.Add(DescribeFileForConflict(loc["FileOps_Conflict_Existing"], conflict.DestinationPath));
        panel.Children.Add(new TextBlock
        {
            Text = loc["FileOps_Conflict_ReplaceNote"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrushes.Get("TextFillColorTertiaryBrush", this)
        });
        var applyAll = new CheckBox { Content = loc["FileOps_Conflict_ApplyAll"], Visibility = _conflictsRepeat ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(applyAll);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = DialogTheme,
            Title = loc["FileOps_Conflict_Title"],
            Content = panel,
            PrimaryButtonText = loc["FileOps_Conflict_Replace"],
            SecondaryButtonText = loc["FileOps_Conflict_KeepBoth"],
            CloseButtonText = loc["FileOps_Conflict_Skip"],
            DefaultButton = ContentDialogButton.Secondary
        };

        ContentDialogResult answer;
        try
        {
            answer = await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Another dialog is open: leave the file alone rather than guess
            Log.Error("Conflict dialog", ex);
            return new ConflictDecision(ConflictChoice.Skip, false);
        }

        var choice = answer switch
        {
            ContentDialogResult.Primary => ConflictChoice.Replace,
            ContentDialogResult.Secondary => ConflictChoice.KeepBoth,
            _ => ConflictChoice.Skip
        };
        return new ConflictDecision(choice, applyAll.IsChecked == true);
    }

    private UIElement DescribeFileForConflict(string label, string path)
    {
        var box = new StackPanel { Spacing = 1 };
        box.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ThemeBrushes.Get("TextFillColorSecondaryBrush", this) });
        string details = string.Empty;
        try
        {
            var clip = GameClip.FromFileInfo(new FileInfo(path));
            details = $"{clip.FormattedFileSize}  ·  {clip.FormattedDate}";
        }
        catch { }
        box.Children.Add(new TextBlock { Text = details, FontSize = 13 });
        return box;
    }

    private async Task ShowTransferErrorsAsync(FileTransferResult result)
    {
        var loc = LayoutLoc;
        var lines = result.Failed
            .Take(8)
            .Select(f => $"{Path.GetFileName(f.Path)}: {DescribeError(f.Error)}")
            .ToList();
        if (result.Failed.Count > 8) lines.Add("…");
        await ShowErrorDialogAsync(loc["FileOps_Error_Title"] + "\n\n" + string.Join("\n", lines));
    }

    private string DescribeError(Exception error) => error switch
    {
        RecycleFailedException => LayoutLoc["FileOps_Error_Recycle"],
        UnauthorizedAccessException => LayoutLoc["FileOps_Error_Access"],
        _ => error.Message
    };
}
