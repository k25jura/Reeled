using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Helpers;
using Reeled.Models;
using Reeled.Services;
using Reeled.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Reeled.Views;

/// <summary>
/// Dragging clips: onto a folder in the sidebar, a folder chip in the top or bottom bar, a folder tab or the
/// open folder moves them (Ctrl copies; the drag caption says which); out of the app they travel as real
/// files (Explorer, Discord, a browser). Video files dragged in from Explorer are copied into the folder
/// they are dropped on (Shift moves them).
/// </summary>
public sealed partial class HomePage
{
    /// <summary>The clips of the drag that started in this view; null for drags that come from other apps.</summary>
    private List<GameClip>? _dragClips;

    /// <summary>The subfolder menu of a folder chip while it is open (a drag can rest on a chip to open it).</summary>
    private MenuFlyout? _openSubfolderMenu;

    private object? _highlightKey;
    private Action<bool>? _highlightSetter;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dwellTimer;
    private object? _dwellKey;

    // ---------------------------------------------------------------------------------------------
    // Drag source
    // ---------------------------------------------------------------------------------------------

    private void OnClipDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var dragged = e.Items.OfType<GameClip>().FirstOrDefault();
        if (dragged == null)
        {
            e.Cancel = true;
            return;
        }

        // Dragging a selected clip takes the whole selection along, any other clip goes alone
        var clips = dragged.IsSelected ? _selection.ToList() : new List<GameClip> { dragged };
        var files = ResolveStorageFiles(clips);
        if (files.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.Data.SetStorageItems(files);
        _dragClips = clips;
        try
        {
            // The drag image shows how many clips are on their way
            foreach (var clip in clips.Where(c => !ReferenceEquals(c, dragged))) e.Items.Add(clip);
        }
        catch { }
    }

    private void OnClipDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        _dragClips = null;
        CancelDwell();
        ClearDropHighlight();
        SetClipsDropOverlay(false);
    }

    /// <summary>Storage files for the clips. Resolved on the thread pool and waited for: a drag can't start later.</summary>
    private static List<IStorageItem> ResolveStorageFiles(IEnumerable<GameClip> clips)
    {
        var list = new List<IStorageItem>();
        foreach (var clip in clips)
        {
            string path = clip.FilePath;
            try
            {
                list.Add(Task.Run(async () => await StorageFile.GetFileFromPathAsync(path)).GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                Log.Error("Drag file", ex);
            }
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // Drop targets: shared logic
    // ---------------------------------------------------------------------------------------------

    private bool IsFileDrag(DragEventArgs e)
    {
        try
        {
            return _dragClips != null || e.DataView.Contains(StandardDataFormats.StorageItems);
        }
        catch
        {
            return false;
        }
    }

    private FileTransferKind DropKindFor(DragEventArgs e)
    {
        bool ctrl = (e.Modifiers & Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control) != 0;
        bool shift = (e.Modifiers & Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift) != 0;
        // Between folders of the library a drop moves (Ctrl copies); files from outside are copied in (Shift moves)
        return _dragClips != null
            ? (ctrl ? FileTransferKind.Copy : FileTransferKind.Move)
            : (shift ? FileTransferKind.Move : FileTransferKind.Copy);
    }

    /// <summary>
    /// Decides what a drop on <paramref name="destination"/> would do and tells the drag so: the cursor, and
    /// a caption such as "Move to Fortnite". Returns false when the drop isn't possible there.
    /// </summary>
    private bool PrepareDrop(DragEventArgs e, string? destination)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        if (!IsFileDrag(e)) return false;

        var loc = LayoutLoc;
        if (destination == null)
        {
            SetDragCaption(e, loc["Drag_NoFolder"]);
            return false;
        }

        var kind = DropKindFor(e);
        // Moving clips to the folder they are all in already does nothing
        if (kind == FileTransferKind.Move && _dragClips != null &&
            _dragClips.All(c => PathUtil.PathEquals(c.DirectoryPath, destination)))
        {
            return false;
        }

        e.AcceptedOperation = kind == FileTransferKind.Copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
        SetDragCaption(e, string.Format(loc[kind == FileTransferKind.Copy ? "Drag_CopyTo" : "Drag_MoveTo"], FolderDisplayName(destination)));
        return true;
    }

    private static void SetDragCaption(DragEventArgs e, string caption)
    {
        if (e.DragUIOverride == null) return;
        e.DragUIOverride.Caption = caption;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    /// <summary>Takes the dropped files into <paramref name="destination"/> (copying or moving, with progress).</summary>
    private async Task HandleDropAsync(DragEventArgs e, string destination)
    {
        var kind = DropKindFor(e);
        var internalPaths = _dragClips?.Select(c => c.FilePath).ToList();

        List<string> paths;
        var deferral = e.GetDeferral();
        try
        {
            if (internalPaths != null)
            {
                paths = internalPaths;
                e.AcceptedOperation = kind == FileTransferKind.Copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            }
            else
            {
                var items = await e.DataView.GetStorageItemsAsync();
                paths = items.OfType<StorageFile>()
                    .Select(f => f.Path)
                    .Where(p => !string.IsNullOrEmpty(p) && PathUtil.IsVideoFile(p))
                    .ToList();
                // This app does the work itself. Answering "move" would make the source (Explorer) delete the originals on
                // its own, even for files that were skipped, so only a copy is reported back.
                e.AcceptedOperation = kind == FileTransferKind.Copy ? DataPackageOperation.Copy : DataPackageOperation.None;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Drop", ex);
            paths = new List<string>();
            e.AcceptedOperation = DataPackageOperation.None;
        }
        finally
        {
            deferral.Complete();
        }

        if (paths.Count == 0)
        {
            ShowFileOpMessage(LayoutLoc["FileOps_NoVideos"], error: true);
            return;
        }
        await RunTransferAsync(paths, destination, kind);
    }

    // ---------------------------------------------------------------------------------------------
    // Highlight of the target under the drag
    // ---------------------------------------------------------------------------------------------

    private void SetHighlight(object key, Action<bool> setter, bool on)
    {
        if (on)
        {
            if (_highlightSetter != null && !ReferenceEquals(_highlightKey, key)) _highlightSetter(false);
            _highlightKey = key;
            _highlightSetter = setter;
            setter(true);
        }
        else
        {
            setter(false);
            if (ReferenceEquals(_highlightKey, key))
            {
                _highlightKey = null;
                _highlightSetter = null;
            }
        }
    }

    private void ClearDropHighlight()
    {
        _highlightSetter?.Invoke(false);
        _highlightKey = null;
        _highlightSetter = null;
    }

    /// <summary>A soft accent wash over a control (a folder chip or a menu entry) while a drop would land on it.</summary>
    private void TintTarget(Control target, bool on)
    {
        if (!on)
        {
            target.ClearValue(Control.BackgroundProperty);
            return;
        }
        var accent = Helpers.ThemeBrushes.Get("AccentFillColorDefaultBrush", target) as SolidColorBrush;
        var color = accent?.Color ?? Microsoft.UI.Colors.DodgerBlue;
        target.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x46, color.R, color.G, color.B));
    }

    /// <summary>Waits a moment over a target before doing something (opening a folder's subfolders).</summary>
    private void ArmDwell(object key, Action action, int milliseconds = 700)
    {
        if (ReferenceEquals(_dwellKey, key)) return;
        CancelDwell();
        _dwellKey = key;
        _dwellTimer ??= DispatcherQueue.CreateTimer();
        _dwellTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _dwellTimer.IsRepeating = false;
        void Fire(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args)
        {
            timer.Tick -= Fire;
            timer.Stop();
            if (ReferenceEquals(_dwellKey, key)) action();
        }
        _dwellTimer.Tick += Fire;
        _dwellTimer.Start();
    }

    private void CancelDwell()
    {
        _dwellKey = null;
        _dwellTimer?.Stop();
    }

    // ---------------------------------------------------------------------------------------------
    // The open folder (empty part of the clip view)
    // ---------------------------------------------------------------------------------------------

    private bool _dropOverlayOn;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dropOverlayHideTimer;

    private void SetClipsDropOverlay(bool on)
    {
        _dropOverlayHideTimer?.Stop();
        if (on == _dropOverlayOn) return;
        _dropOverlayOn = on;
        if (on) ShowFloating(ClipsDropOverlay, rise: false);
        else HideFloating(ClipsDropOverlay, rise: false);
    }

    private void OnClipsAreaDragOver(object sender, DragEventArgs e)
    {
        string? folder = ViewModel.CurrentFolderPath;
        bool ok = PrepareDrop(e, folder);
        _dropOverlayHideTimer?.Stop();
        SetClipsDropOverlay(ok);
        if (IsFileDrag(e)) e.Handled = true;
    }

    private void OnClipsAreaDragLeave(object sender, DragEventArgs e)
    {
        // Moving between the cards also raises "leave": only hide if no "over" follows right away
        _dropOverlayHideTimer ??= DispatcherQueue.CreateTimer();
        _dropOverlayHideTimer.Interval = TimeSpan.FromMilliseconds(120);
        _dropOverlayHideTimer.IsRepeating = false;
        _dropOverlayHideTimer.Tick -= OnDropOverlayHideTick;
        _dropOverlayHideTimer.Tick += OnDropOverlayHideTick;
        _dropOverlayHideTimer.Start();
    }

    private void OnDropOverlayHideTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        SetClipsDropOverlay(false);
    }

    private async void OnClipsAreaDrop(object sender, DragEventArgs e)
    {
        SetClipsDropOverlay(false);
        string? folder = ViewModel.CurrentFolderPath;
        if (folder == null || !IsFileDrag(e)) return;
        e.Handled = true;
        await HandleDropAsync(e, folder);
    }

    // ---------------------------------------------------------------------------------------------
    // Folders in the sidebar tree
    // ---------------------------------------------------------------------------------------------

    private static DirectoryNode? NodeOfRow(object sender) =>
        sender is TreeViewItem row ? (row.DataContext as DirectoryNode) ?? (row.Content as DirectoryNode) : null;

    private void SetRowHighlight(TreeViewItem row, bool on)
    {
        if (FindVisualChildByName<FrameworkElement>(row, "DropHighlight") is { } highlight) highlight.Opacity = on ? 1 : 0;
    }

    private void OnFolderRowDragOver(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem row || NodeOfRow(sender) is not { } node || !IsFileDrag(e)) return;
        bool ok = PrepareDrop(e, node.FullPath);
        SetHighlight(row, on => SetRowHighlight(row, on), ok);
        // Like Explorer: hovering a closed folder opens it so its subfolders can be reached
        if (ok && node.HasSubDirectories && !node.IsExpanded) ArmDwell(row, () => ExpandDirectoryWithAnimation(node));
        else CancelDwell();
        e.Handled = true;
    }

    private void OnFolderRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem row) return;
        SetHighlight(row, on => SetRowHighlight(row, on), false);
        CancelDwell();
    }

    private async void OnFolderRowDrop(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem row) return;
        SetHighlight(row, on => SetRowHighlight(row, on), false);
        CancelDwell();
        if (NodeOfRow(sender) is not { } node || !IsFileDrag(e)) return;
        e.Handled = true;
        await HandleDropAsync(e, node.FullPath);
    }

    // ---------------------------------------------------------------------------------------------
    // Folder chips of the top / bottom bar and their subfolder menu
    // ---------------------------------------------------------------------------------------------

    /// <summary>Makes a bar item (or a menu entry) a place to drop clips onto.</summary>
    private void WireFolderDropTarget(Control target, Func<DirectoryNode?> node, Action? dwell = null)
    {
        target.AllowDrop = true;
        target.DragOver += (s, e) =>
        {
            if (!IsFileDrag(e)) return;
            var n = node();
            bool ok = n != null && PrepareDrop(e, n.FullPath);
            SetHighlight(target, on => TintTarget(target, on), ok);
            if (ok && dwell != null) dwell();
            else if (!ok) CancelDwell();
            e.Handled = true;
        };
        target.DragLeave += (s, e) => SetHighlight(target, on => TintTarget(target, on), false);
        target.Drop += async (s, e) =>
        {
            SetHighlight(target, on => TintTarget(target, on), false);
            CancelDwell();
            var n = node();
            if (n == null || !IsFileDrag(e)) return;
            e.Handled = true;
            _openSubfolderMenu?.Hide();
            await HandleDropAsync(e, n.FullPath);
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Tabs
    // ---------------------------------------------------------------------------------------------

    private void AttachTabDrop(TabViewItem item)
    {
        string? Destination() => item.Tag is LibraryTab { Section: NavigationSection.Folder, FolderPath: { } path } ? path : null;
        void Tint(bool on)
        {
            if (item.Header is not Grid header) return;
            if (!on)
            {
                header.Background = null;
                return;
            }
            var accent = Helpers.ThemeBrushes.Get("AccentFillColorDefaultBrush", item) as SolidColorBrush;
            var color = accent?.Color ?? Microsoft.UI.Colors.DodgerBlue;
            header.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x46, color.R, color.G, color.B));
        }

        item.AllowDrop = true;
        item.DragOver += (s, e) =>
        {
            // Dragging tabs around is the strip's own business
            if (!IsFileDrag(e)) return;
            bool ok = PrepareDrop(e, Destination());
            SetHighlight(item, Tint, ok);
            e.Handled = true;
        };
        item.DragLeave += (s, e) => SetHighlight(item, Tint, false);
        item.Drop += async (s, e) =>
        {
            SetHighlight(item, Tint, false);
            var destination = Destination();
            if (destination == null || !IsFileDrag(e)) return;
            e.Handled = true;
            await HandleDropAsync(e, destination);
        };
    }
}
