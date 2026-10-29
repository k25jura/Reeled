using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Services;
using Reeled.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

namespace Reeled;

/// <summary>
/// Files dragged in from outside: a full-window overlay with two zones, "Open" (play) and "Add to library"
/// (copy into the open folder / add a folder). And the floating caption pill that follows the pointer for
/// every drag over the window and stands in for the system's caption and glyph.
/// </summary>
public sealed partial class MainWindow
{
    private enum DropZone { None, Open, Add }

    private DropZone _dropZone = DropZone.None;
    private int _dragInfoGeneration;
    private bool _dragInfoRequested;
    private bool _dragInfoKnown;
    private bool _dragHasFiles;
    private bool _dragHasFolders;

    private readonly SolidColorBrush _openStroke = new();
    private readonly SolidColorBrush _openTint = new();
    private readonly SolidColorBrush _openBadge = new();
    private readonly SolidColorBrush _openIcon = new();
    private readonly SolidColorBrush _addStroke = new();
    private readonly SolidColorBrush _addTint = new();
    private readonly SolidColorBrush _addBadge = new();
    private readonly SolidColorBrush _addIcon = new();
    private Color _openColor = Colors.DodgerBlue;
    private Color _addColor = Colors.SeaGreen;
    private bool _addZoneEnabled = true;
    private Storyboard? _zoneStoryboard;

    private AcrylicBrush? _pillAcrylic;
    private bool _pillShown;
    private Storyboard? _pillStoryboard;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pillHideTimer;

    private const byte StrokeIdle = 0x90;
    private const byte StrokeActive = 0xFF;
    private const byte TintActive = 0x38;

    private void WireDragDrop()
    {
        OpenZoneRect.Stroke = _openStroke;
        OpenZoneTint.Background = _openTint;
        OpenZoneBadge.Background = _openBadge;
        OpenZoneIcon.Foreground = _openIcon;
        AddZoneRect.Stroke = _addStroke;
        AddZoneTint.Background = _addTint;
        AddZoneBadge.Background = _addBadge;
        AddZoneIcon.Foreground = _addIcon;
        ApplyZoneColors();

        // These run after every element below has spoken (handled or not): that is where the drop zones decide the
        // answer for outside drags and where the pill takes over from the system caption.
        RootWindowGrid.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnAnyDragOver), true);
        RootWindowGrid.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler((s, e) => SchedulePillHide()), true);
        RootWindowGrid.AddHandler(UIElement.DropEvent, new DragEventHandler((s, e) => HideDragPill()), true);
        InternalDrag.Ended += () => DispatcherQueue.TryEnqueue(HideDragPill);
    }

    private static bool HasStorageItems(DragEventArgs e)
    {
        try { return e.DataView.Contains(StandardDataFormats.StorageItems); }
        catch { return false; }
    }

    private static void HideSystemCaption(DragEventArgs e)
    {
        if (e.DragUIOverride == null) return;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void OnAnyDragOver(object sender, DragEventArgs e)
    {
        (string Text, string Glyph, bool Allowed)? pill;
        if (InternalDrag.Active)
        {
            // Clips of the library: the target under the pointer (page code) has set the answer and the text
            HideSystemCaption(e);
            pill = InternalDrag.TakePill();
        }
        else if (HasStorageItems(e))
        {
            InternalDrag.TakePill();
            HideSystemCaption(e);
            if (!_isDropOverlayVisible) ShowDropOverlay(true);
            RequestDragInfo(e.DataView);

            var zone = ZoneAt(e);
            SetDropZone(zone);
            var loc = App.GetService<ILocalizationService>();
            if (zone == DropZone.Add && !CanAdd())
            {
                e.AcceptedOperation = DataPackageOperation.None;
                pill = (loc?["DropOverlay_AddNeedFolder"] ?? "Open a folder first", InternalDrag.GlyphBlocked, false);
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                if (e.DragUIOverride != null)
                {
                    e.DragUIOverride.Caption = zone == DropZone.Add ? (loc?["Drag_AddToLibrary"] ?? "Add to library") : (loc?["Drag_Open"] ?? "Open");
                }
                pill = zone == DropZone.Add
                    ? (loc?["Drag_AddToLibrary"] ?? "Add to library", InternalDrag.GlyphAdd, true)
                    : (loc?["Drag_Open"] ?? "Open", InternalDrag.GlyphOpen, true);
            }
            e.Handled = true;
        }
        else
        {
            // Something else (a tab being reordered): not ours
            InternalDrag.TakePill();
            return;
        }

        if (pill is { } p) ShowDragPill(p.Text, p.Glyph, p.Allowed, e.GetPosition(RootWindowGrid));
        else HideDragPill();
    }

    // ---------------------------------------------------------------------------------------------
    // Zones
    // ---------------------------------------------------------------------------------------------

    private DropZone ZoneAt(DragEventArgs e)
    {
        var p = e.GetPosition(RootWindowGrid);
        return p.X < RootWindowGrid.ActualWidth / 2 ? DropZone.Open : DropZone.Add;
    }

    /// <summary>The folder "Add to library" copies files into: the open folder of the library (not while the player is up).</summary>
    private string? CurrentDropFolder() =>
        PlayerOverlayContainer.Visibility == Visibility.Visible ? null : (RootFrame.Content as HomePage)?.CurrentDropFolder;

    /// <summary>Folders can always be added; video files need an open folder to land in.</summary>
    private bool CanAdd()
    {
        if (!_dragInfoKnown) return true;
        return _dragHasFolders || (_dragHasFiles && CurrentDropFolder() != null);
    }

    private void RequestDragInfo(DataPackageView view)
    {
        if (_dragInfoRequested) return;
        _dragInfoRequested = true;
        int generation = _dragInfoGeneration;
        _ = LoadAsync();

        async Task LoadAsync()
        {
            try
            {
                var items = await view.GetStorageItemsAsync();
                if (generation != _dragInfoGeneration) return;
                _dragHasFolders = items.Any(i => i is StorageFolder || (i is not StorageFile && !string.IsNullOrEmpty(i.Path) && System.IO.Directory.Exists(i.Path)));
                _dragHasFiles = items.Any(i => i is StorageFile);
                _dragInfoKnown = true;
                RefreshAddZone();
            }
            catch
            {
                // The source can't be inspected early: stay permissive, the drop decides
            }
        }
    }

    private void ResetDragInfo()
    {
        _dragInfoGeneration++;
        _dragInfoRequested = false;
        _dragInfoKnown = false;
        _dragHasFiles = false;
        _dragHasFolders = false;
    }

    private static Color BrushColor(Brush brush, Color fallback) => (brush as SolidColorBrush)?.Color ?? fallback;

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Open takes the accent, Add the success tone of the current theme; the zones start out idle.</summary>
    private void ApplyZoneColors()
    {
        _openColor = BrushColor(Helpers.ThemeBrushes.Get("AccentFillColorDefaultBrush", RootWindowGrid), Colors.DodgerBlue);
        _addColor = BrushColor(Helpers.ThemeBrushes.Get("SystemFillColorSuccessBrush", RootWindowGrid), Colors.SeaGreen);
        _zoneStoryboard?.Stop();
        _zoneStoryboard = null;
        _dropZone = DropZone.None;
        SnapZones();
        AddZoneContent.Opacity = _addZoneEnabled ? 1 : 0.55;
    }

    private (Color Stroke, Color Tint) ZoneTargets(bool isOpen, bool active)
    {
        var color = isOpen ? _openColor : _addColor;
        if (!isOpen && !_addZoneEnabled)
        {
            var gray = BrushColor(Helpers.ThemeBrushes.Get("TextFillColorDisabledBrush", RootWindowGrid), Colors.Gray);
            return (WithAlpha(gray, 0x90), WithAlpha(gray, 0));
        }
        return (WithAlpha(color, active ? StrokeActive : StrokeIdle), WithAlpha(color, active ? TintActive : (byte)0));
    }

    private void SnapZones()
    {
        var (os, ot) = ZoneTargets(true, _dropZone == DropZone.Open);
        var (as_, at) = ZoneTargets(false, _dropZone == DropZone.Add);
        _openStroke.Color = os;
        _openTint.Color = ot;
        _addStroke.Color = as_;
        _addTint.Color = at;
        _openBadge.Color = WithAlpha(_openColor, 0x2A);
        _openIcon.Color = _openColor;
        var addBase = _addZoneEnabled ? _addColor : BrushColor(Helpers.ThemeBrushes.Get("TextFillColorDisabledBrush", RootWindowGrid), Colors.Gray);
        _addBadge.Color = WithAlpha(addBase, 0x2A);
        _addIcon.Color = addBase;
    }

    private void SetDropZone(DropZone zone)
    {
        if (zone == _dropZone) return;
        _dropZone = zone;
        AnimateZones();
    }

    /// <summary>Colour transition of both zones to their current state (stroke and wash), on the shared curves.</summary>
    private void AnimateZones()
    {
        // An interrupted animation keeps the colour it had reached
        var colors = new[] { _openStroke.Color, _openTint.Color, _addStroke.Color, _addTint.Color };
        _zoneStoryboard?.Stop();
        _openStroke.Color = colors[0];
        _openTint.Color = colors[1];
        _addStroke.Color = colors[2];
        _addTint.Color = colors[3];

        var (os, ot) = ZoneTargets(true, _dropZone == DropZone.Open);
        var (as_, at) = ZoneTargets(false, _dropZone == DropZone.Add && _addZoneEnabled);
        var sb = new Storyboard();
        void Add(SolidColorBrush brush, Color to)
        {
            var anim = new ColorAnimation
            {
                From = brush.Color,
                To = to,
                Duration = Helpers.Motion.Duration(Helpers.Motion.Short),
                EasingFunction = Helpers.Motion.Decelerate,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(anim, brush);
            Storyboard.SetTargetProperty(anim, "Color");
            sb.Children.Add(anim);
        }
        Add(_openStroke, os);
        Add(_openTint, ot);
        Add(_addStroke, as_);
        Add(_addTint, at);
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_zoneStoryboard, sb)) return;
            sb.Stop();
            SnapZones();
        };
        _zoneStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    /// <summary>The Add zone dims (and says why) when the drag holds videos only and no folder is open.</summary>
    private void RefreshAddZone()
    {
        bool enabled = CanAdd();
        UpdateDropOverlayLocalization();
        if (enabled == _addZoneEnabled) return;
        _addZoneEnabled = enabled;
        AddZoneContent.Opacity = enabled ? 1 : 0.55;
        UpdateDropOverlayLocalization();
        SnapZonesBadges();
        AnimateZones();
    }

    private void SnapZonesBadges()
    {
        var addBase = _addZoneEnabled ? _addColor : BrushColor(Helpers.ThemeBrushes.Get("TextFillColorDisabledBrush", RootWindowGrid), Colors.Gray);
        _addBadge.Color = WithAlpha(addBase, 0x2A);
        _addIcon.Color = addBase;
    }

    private void PrepareZonesForShow()
    {
        _addZoneEnabled = true;
        ApplyZoneColors();
        UpdateDropOverlayLocalization();
    }

    // ---------------------------------------------------------------------------------------------
    // Pill
    // ---------------------------------------------------------------------------------------------

    private void ShowDragPill(string text, string glyph, bool allowed, Windows.Foundation.Point pointer)
    {
        _pillHideTimer?.Stop();

        if (_pillAcrylic == null)
        {
            _pillAcrylic = new AcrylicBrush();
            DragPill.Background = _pillAcrylic;
        }
        Helpers.Backdrop.ApplyAppSurface(_pillAcrylic, RootWindowGrid.ActualTheme);

        DragPillText.Text = text;
        DragPillIcon.Glyph = glyph;
        DragPillIcon.Foreground = Helpers.ThemeBrushes.Get(allowed ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush", RootWindowGrid);

        bool wasShown = _pillShown;
        _pillShown = true;
        DragPillHost.Visibility = Visibility.Visible;
        DragPillHost.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = DragPillHost.DesiredSize.Width, h = DragPillHost.DesiredSize.Height;
        double maxX = RootWindowGrid.ActualWidth, maxY = RootWindowGrid.ActualHeight;

        // Below and right of the pointer; flipped to the other side near the edges of the window
        double x = pointer.X + 18, y = pointer.Y + 22;
        if (x + w > maxX - 6) x = pointer.X - 14 - w;
        if (y + h > maxY - 6) y = pointer.Y - 12 - h;
        DragPillMove.X = Math.Max(6, x);
        DragPillMove.Y = Math.Max(6, y);

        if (!wasShown) AnimatePill(true);
        else if (_pillStoryboard == null) DragPill.Opacity = 1;
    }

    private void SchedulePillHide()
    {
        _pillHideTimer ??= DispatcherQueue.CreateTimer();
        _pillHideTimer.Interval = TimeSpan.FromMilliseconds(120);
        _pillHideTimer.IsRepeating = false;
        _pillHideTimer.Tick -= OnPillHideTick;
        _pillHideTimer.Tick += OnPillHideTick;
        _pillHideTimer.Start();
    }

    private void OnPillHideTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        HideDragPill();
    }

    private void HideDragPill()
    {
        _pillHideTimer?.Stop();
        if (!_pillShown) return;
        _pillShown = false;
        AnimatePill(false);
    }

    private void AnimatePill(bool show)
    {
        double opacity = DragPill.Opacity, scale = DragPillScale.ScaleX;
        _pillStoryboard?.Stop();
        DragPill.Opacity = opacity;
        DragPillScale.ScaleX = scale;
        DragPillScale.ScaleY = scale;

        var sb = new Storyboard();
        double ms = show ? Helpers.Motion.Short : Helpers.Motion.Micro + 20;
        sb.Children.Add(Helpers.Motion.Spline(DragPill, "Opacity", show ? 1 : 0, ms, from: opacity, accelerate: !show));
        sb.Children.Add(Helpers.Motion.Spline(DragPillScale, "ScaleX", show ? 1 : 0.92, ms, from: scale, accelerate: !show));
        sb.Children.Add(Helpers.Motion.Spline(DragPillScale, "ScaleY", show ? 1 : 0.92, ms, from: scale, accelerate: !show));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_pillStoryboard, sb)) return;
            _pillStoryboard = null;
            sb.Stop();
            DragPill.Opacity = show ? 1 : 0;
            DragPillScale.ScaleX = DragPillScale.ScaleY = show ? 1 : 0.92;
            if (!show && !_pillShown) DragPillHost.Visibility = Visibility.Collapsed;
        };
        _pillStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }
}
