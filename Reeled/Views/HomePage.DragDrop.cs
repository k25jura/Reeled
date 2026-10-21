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

/// <summary>True while clips dragged from the library are in flight (the window shows no external-drop overlay then).</summary>
internal static class InternalDrag
{
    public static bool Active;

    /// <summary>Raised when an internal drag is over (dropped or cancelled) so the window can hide its drag pill.</summary>
    public static event Action? Ended;

    public static void RaiseEnded() => Ended?.Invoke();

    /// <summary>What the target under the pointer says a drop would do; read (and cleared) by the window once per DragOver.</summary>
    private static (string Text, string Glyph, bool Allowed)? _pill;

    public const string GlyphMove = "";
    public const string GlyphCopy = "";
    public const string GlyphAdd = "";
    public const string GlyphOpen = "";
    public const string GlyphBlocked = "";

    public static void SetPill(string text, string glyph, bool allowed) => _pill = (text, glyph, allowed);

    public static (string Text, string Glyph, bool Allowed)? TakePill()
    {
        var pill = _pill;
        _pill = null;
        return pill;
    }
}

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
        _dragGhostClip = dragged;
        InternalDrag.Active = true;
    }

    private GameClip? _dragGhostClip;

    /// <summary>Hooks the drag image of a clip container (once): a small thumbnail instead of the whole card or row.</summary>
    private void HookClipDragVisual(Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container)
    {
        if (_dragHooked.TryGetValue(container, out _)) return;
        _dragHooked.Add(container, new object());
        container.DragStarting += OnClipContainerDragStarting;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.Controls.Primitives.SelectorItem, object> _dragHooked = new();

    private async void OnClipContainerDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var clip = ActiveClipsView.ItemFromContainer(sender) as GameClip;
            if (clip != null)
            {
                int count = clip.IsSelected ? Math.Max(1, _selection.Count) : 1;
                var bitmap = await RenderDragGhostAsync(clip, count);
                if (bitmap != null) args.DragUI.SetContentFromSoftwareBitmap(bitmap, new Windows.Foundation.Point(14, 14));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Drag image", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>A 96x54 rounded thumbnail of the clip with a count badge for several clips, rendered off-screen.</summary>
    private async Task<Windows.Graphics.Imaging.SoftwareBitmap?> RenderDragGhostAsync(GameClip clip, int count)
    {
        var ghost = new Grid { Width = 96, Height = 54, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-4000, 0, 0, 0), IsHitTestVisible = false };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = Helpers.ThemeBrushes.Get("SolidBackgroundFillColorSecondaryBrush", this),
            BorderBrush = Helpers.ThemeBrushes.Get("CardStrokeColorDefaultBrush", this),
            BorderThickness = new Thickness(1)
        };
        if (clip.Thumbnail != null)
        {
            frame.Child = new Image { Source = clip.Thumbnail, Stretch = Stretch.UniformToFill };
        }
        else
        {
            frame.Child = new FontIcon { Glyph = "", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", this) };
        }
        ghost.Children.Add(frame);
        if (count > 1)
        {
            ghost.Children.Add(new Border
            {
                MinWidth = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 4),
                Background = Helpers.ThemeBrushes.Get("AccentFillColorDefaultBrush", this),
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    Foreground = Helpers.ThemeBrushes.Get("TextOnAccentFillColorPrimaryBrush", this)
                }
            });
        }

        ClipsHost.Children.Add(ghost);
        try
        {
            ghost.UpdateLayout();
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(ghost);
            var pixels = await rtb.GetPixelsAsync();
            if (rtb.PixelWidth <= 0 || rtb.PixelHeight <= 0) return null;
            return Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                pixels, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, rtb.PixelWidth, rtb.PixelHeight,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            ClipsHost.Children.Remove(ghost);
        }
    }

    private void OnClipDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        InternalDrag.Active = false;
        _dragClips = null;
        CancelDwell();
        ClearDropHighlight();
        SetClipsDropOverlay(false);
        InternalDrag.RaiseEnded();
    }

    /// <summary>The folder the library shows right now (the target of "Add to library" for dropped files), or null.</summary>
    public string? CurrentDropFolder => ViewModel.CurrentFolderPath;

    /// <summary>Copies video files dropped from outside into the open folder (the "Add to library" drop zone).</summary>
    public async Task AddExternalFilesAsync(IReadOnlyList<string> files)
    {
        string? folder = ViewModel.CurrentFolderPath;
        var videos = files.Where(PathUtil.IsVideoFile).ToList();
        if (folder == null || videos.Count == 0)
        {
            ShowFileOpMessage(LayoutLoc[folder == null ? "FileOps_NeedFolder" : "FileOps_NoVideos"], error: true);
            return;
        }
        await RunTransferAsync(videos, folder, FileTransferKind.Copy);
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
            SetDragCaption(e, loc["Drag_NoFolder"], InternalDrag.GlyphBlocked, allowed: false);
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
        bool copy = kind == FileTransferKind.Copy;
        SetDragCaption(e, string.Format(loc[copy ? "Drag_CopyTo" : "Drag_MoveTo"], FolderDisplayName(destination)),
            copy ? InternalDrag.GlyphCopy : InternalDrag.GlyphMove, allowed: true);
        return true;
    }

    /// <summary>
    /// Describes the drop for the in-app drag pill (see MainWindow) and keeps the system caption and glyph hidden:
    /// the pill replaces them while the pointer is over Reeled.
    /// </summary>
    private static void SetDragCaption(DragEventArgs e, string caption, string glyph, bool allowed)
    {
        InternalDrag.SetPill(caption, glyph, allowed);
        if (e.DragUIOverride == null) return;
        e.DragUIOverride.Caption = caption;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
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
        // Drags from outside get the window's Open / Add overlay, drags from this view only highlight their targets
        SetClipsDropOverlay(false);
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

    /// <summary>A tab is a drop target through the strip-level handlers (<see cref="WireTabStripDrop"/>); the item only has to allow drops.</summary>
    private void AttachTabDrop(TabViewItem item)
    {
        item.AllowDrop = true;
    }

    private static string? TabDropDestination(TabViewItem item) =>
        item.Tag is LibraryTab { Section: NavigationSection.Folder, FolderPath: { } path } ? path : null;

    private void TintTab(TabViewItem item, bool on)
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

    /// <summary>The tab under the pointer of a drag over the strip (hit-tested by bounds, whatever element raised the event).</summary>
    private TabViewItem? TabItemUnderDrag(DragEventArgs e)
    {
        foreach (var item in _tabItems.Values)
        {
            if (item.ActualWidth <= 0) continue;
            var p = e.GetPosition(item);
            if (p.X >= 0 && p.Y >= 0 && p.X <= item.ActualWidth && p.Y <= item.ActualHeight) return item;
        }
        return null;
    }

    private void ClearTabHighlight()
    {
        if (_highlightKey is TabViewItem) ClearDropHighlight();
    }

    /// <summary>
    /// The tab strip's own list handles drags (to reorder tabs) and answers "not allowed" for anything else
    /// after the items have spoken, which made tabs refuse files until the pointer had rested on them. These
    /// handlers sit on the TabView, hear the event last (even when it was handled below) and decide
    /// synchronously, so a folder tab accepts from the first DragEnter on.
    /// </summary>
    private void WireTabStripDrop()
    {
        _tabView.AllowDrop = true;
        DragEventHandler over = (s, e) =>
        {
            if (!IsFileDrag(e)) return;
            var item = TabItemUnderDrag(e);
            if (item == null)
            {
                ClearTabHighlight();
                return;
            }
            string? destination = TabDropDestination(item);
            if (destination == null)
            {
                // Home, Favorites and Moments are not places to drop into
                e.AcceptedOperation = DataPackageOperation.None;
                if (e.DragUIOverride != null)
                {
                    e.DragUIOverride.IsCaptionVisible = false;
                    e.DragUIOverride.IsGlyphVisible = false;
                }
                ClearTabHighlight();
                e.Handled = true;
                return;
            }
            bool ok = PrepareDrop(e, destination);
            SetHighlight(item, on => TintTab(item, on), ok);
            e.Handled = true;
        };
        _tabView.AddHandler(UIElement.DragEnterEvent, over, true);
        _tabView.AddHandler(UIElement.DragOverEvent, over, true);
        _tabView.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler((s, e) => ClearTabHighlight()), true);
        _tabView.AddHandler(UIElement.DropEvent, new DragEventHandler(async (s, e) =>
        {
            ClearTabHighlight();
            if (!IsFileDrag(e)) return;
            var item = TabItemUnderDrag(e);
            string? destination = item != null ? TabDropDestination(item) : null;
            if (destination == null) return;
            e.Handled = true;
            await HandleDropAsync(e, destination);
        }), true);
    }
}
