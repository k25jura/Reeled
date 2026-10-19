using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;

namespace Reeled.Controls;

/// <summary>
/// Drag-and-drop editor for the player's bottom bar (VLC's toolbar editor, our styling): three zones
/// that mirror the bar, a tray of unused buttons, and the Classic / Compact switch. Every change is
/// reported through <see cref="LayoutChanged"/> so the bar behind it updates live.
/// </summary>
public sealed class ToolbarEditor : StackPanel
{
    public sealed class Chip
    {
        public required string Id { get; init; }
        public required string Glyph { get; init; }
        public required string Label { get; init; }
    }

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["play"] = "", ["prev"] = "", ["next"] = "", ["moments"] = "", ["repeat"] = "",
        ["shuffle"] = "", ["tracks"] = "", ["queue"] = "", ["volume"] = "", ["fullscreen"] = "",
        ["speed"] = "", ["session"] = "", ["snapshot"] = "", ["pip"] = "", ["stop"] = "",
        ["skipback"] = "", ["skipforward"] = "", ["frameback"] = "", ["frameforward"] = "",
    };

    private readonly ObservableCollection<Chip> _left = new();
    private readonly ObservableCollection<Chip> _center = new();
    private readonly ObservableCollection<Chip> _right = new();
    private readonly ObservableCollection<Chip> _tray = new();
    private readonly ListView _leftList, _centerList, _rightList, _trayList;
    private readonly Button _classicButton, _compactButton;
    private readonly TextBlock _trayLabel, _hint;
    private ToolbarLayoutMode _mode;
    private ListView? _dragSource;
    private Chip? _dragChip;
    private bool _suppress;

    public event EventHandler<PlayerToolbarLayout>? LayoutChanged;

    public string[] AllIds { get; set; } = Array.Empty<string>();

    public ToolbarEditor()
    {
        Spacing = 12;
        var loc = App.GetService<Services.ILocalizationService>();

        // Mode switch
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _classicButton = ModeButton(loc["Toolbar_ModeClassic"], ToolbarLayoutMode.Classic);
        _compactButton = ModeButton(loc["Toolbar_ModeCompact"], ToolbarLayoutMode.Compact);
        modeRow.Children.Add(_classicButton);
        modeRow.Children.Add(_compactButton);
        Children.Add(modeRow);

        // The bar: three zones side by side, like the real one
        var bar = new Grid
        {
            ColumnSpacing = 6,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["LayerFillColorAltBrush"]
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _leftList = ZoneList(_left, loc["Toolbar_ZoneLeft"]);
        _centerList = ZoneList(_center, loc["Toolbar_ZoneCenter"]);
        _rightList = ZoneList(_right, loc["Toolbar_ZoneRight"]);
        AddZone(bar, _leftList, 0, loc["Toolbar_ZoneLeft"]);
        AddZone(bar, _centerList, 1, loc["Toolbar_ZoneCenter"]);
        AddZone(bar, _rightList, 2, loc["Toolbar_ZoneRight"]);
        Children.Add(bar);

        _trayLabel = new TextBlock
        {
            Text = loc["Toolbar_Available"],
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(2, 4, 0, -4)
        };
        Children.Add(_trayLabel);
        _trayList = ZoneList(_tray, loc["Toolbar_Available"], wrap: true);
        var trayHost = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = _trayList,
            MinHeight = 52
        };
        Children.Add(trayHost);

        _hint = new TextBlock
        {
            Text = loc["Toolbar_Hint"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        };
        Children.Add(_hint);
    }

    private Button ModeButton(string text, ToolbarLayoutMode mode)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 5, 14, 6), FontSize = 12.5 };
        button.Click += (s, e) =>
        {
            if (_mode == mode) return;
            _mode = mode;
            UpdateModeButtons();
            Raise();
        };
        return button;
    }

    private void UpdateModeButtons()
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        _classicButton.Style = _mode == ToolbarLayoutMode.Classic ? accent : null;
        _compactButton.Style = _mode == ToolbarLayoutMode.Compact ? accent : null;
    }

    private static void AddZone(Grid bar, ListView list, int column, string label)
    {
        var zone = new Grid
        {
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            MinHeight = 46
        };
        zone.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            Opacity = 0.55,
            Margin = new Thickness(8, 4, 0, 0),
            IsHitTestVisible = false,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        list.Margin = new Thickness(0, 16, 0, 0);
        zone.Children.Add(list);
        Grid.SetColumn(zone, column);
        bar.Children.Add(zone);
    }

    private ListView ZoneList(ObservableCollection<Chip> items, string name, bool wrap = false)
    {
        var list = new ListView
        {
            ItemsSource = items,
            SelectionMode = ListViewSelectionMode.None,
            CanDragItems = true,
            CanReorderItems = true,
            AllowDrop = true,
            IsItemClickEnabled = true,
            MinHeight = 34,
            Padding = new Thickness(2),
            ItemTemplate = wrap ? ChipTemplate() : IconChipTemplate(),
            ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(wrap
                ? "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' ItemWidth='170' /></ItemsPanelTemplate>"
                : "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel Orientation='Horizontal' /></ItemsPanelTemplate>"),
        };
        ScrollViewer.SetHorizontalScrollMode(list, wrap ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
        ScrollViewer.SetVerticalScrollMode(list, wrap ? ScrollMode.Auto : ScrollMode.Disabled);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, name);
        list.ItemContainerStyle = ChipContainerStyle();

        list.DragItemsStarting += (s, e) =>
        {
            _dragSource = list;
            _dragChip = e.Items.FirstOrDefault() as Chip;
            e.Data.SetText(_dragChip?.Id ?? string.Empty);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        };
        // handledEventsToo: a reorderable ListView handles drag events for itself
        list.AddHandler(UIElement.DragEnterEvent, new DragEventHandler((s, e) =>
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move), true);
        list.AddHandler(UIElement.DragOverEvent, new DragEventHandler((s, e) =>
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move), true);
        list.AddHandler(UIElement.DropEvent, new DragEventHandler((s, e) => OnDrop(list, items, e)), true);
        // Clicking a button offers the same moves without dragging (and works from the keyboard)
        list.ItemClick += (s, e) =>
        {
            if (e.ClickedItem is Chip chip && list.ContainerFromItem(chip) is FrameworkElement container)
            {
                ShowChipMenu(list, items, chip, container);
            }
        };
        list.DragItemsCompleted += (s, e) =>
        {
            _dragSource = null;
            _dragChip = null;
            Raise();
        };
        return list;
    }

    /// <summary>Buttons on the bar show their icon only, like the real bar; the name is the tooltip.</summary>
    private static DataTemplate IconChipTemplate() => (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
        "<Border CornerRadius='5' Width='36' Height='32' Background='{ThemeResource ControlFillColorDefaultBrush}' " +
        "BorderBrush='{ThemeResource ControlStrokeColorDefaultBrush}' BorderThickness='1' ToolTipService.ToolTip='{Binding Label}'>" +
        "<FontIcon Glyph='{Binding Glyph}' FontSize='13' />" +
        "</Border></DataTemplate>");

    private static DataTemplate ChipTemplate() => (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
        "<Border CornerRadius='5' Padding='9,5,10,6' Width='164' Background='{ThemeResource ControlFillColorDefaultBrush}' " +
        "BorderBrush='{ThemeResource ControlStrokeColorDefaultBrush}' BorderThickness='1'>" +
        "<StackPanel Orientation='Horizontal' Spacing='7'>" +
        "<FontIcon Glyph='{Binding Glyph}' FontSize='12' />" +
        "<TextBlock Text='{Binding Label}' FontSize='12' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' />" +
        "</StackPanel></Border></DataTemplate>");

    private static Style ChipContainerStyle()
    {
        var style = new Style(typeof(ListViewItem)) { BasedOn = (Style)Application.Current.Resources["DefaultListViewItemStyle"] };
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
        return style;
    }

    private void OnDrop(ListView target, ObservableCollection<Chip> targetItems, DragEventArgs e)
    {
        // Reordering inside a list is handled by the ListView itself
        if (_dragSource == null || _dragChip == null || ReferenceEquals(_dragSource, target)) return;
        var sourceItems = (ObservableCollection<Chip>)_dragSource.ItemsSource;

        // Play can't be put away: the bar always needs it
        if (_dragChip.Id == "play" && ReferenceEquals(target, _trayList)) return;

        int index = targetItems.Count;
        var pos = e.GetPosition(target);
        for (int i = 0; i < targetItems.Count; i++)
        {
            if (target.ContainerFromIndex(i) is FrameworkElement c)
            {
                var p = c.TransformToVisual(target).TransformPoint(new Windows.Foundation.Point(0, 0));
                bool before = target == _trayList
                    ? (pos.Y < p.Y + c.ActualHeight && pos.X < p.X + c.ActualWidth / 2)
                    : pos.X < p.X + c.ActualWidth / 2;
                if (before) { index = i; break; }
            }
        }
        sourceItems.Remove(_dragChip);
        targetItems.Insert(Math.Min(index, targetItems.Count), _dragChip);
        Raise();
    }

    private void ShowChipMenu(ListView list, ObservableCollection<Chip> items, Chip chip, FrameworkElement anchor)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var menu = new MenuFlyout();
        void Move(ObservableCollection<Chip> target, int index)
        {
            items.Remove(chip);
            target.Insert(Math.Clamp(index, 0, target.Count), chip);
            Raise();
        }
        MenuFlyoutItem Entry(string text, string glyph, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            item.Click += (s, e) => action();
            return item;
        }

        var zones = new[] { (_left, loc["Toolbar_ZoneLeft"]), (_center, loc["Toolbar_ZoneCenter"]), (_right, loc["Toolbar_ZoneRight"]) };
        if (ReferenceEquals(items, _tray))
        {
            foreach (var (zone, name) in zones)
            {
                var z = zone;
                menu.Items.Add(Entry(string.Format(loc["Toolbar_AddTo"], name), "", () => Move(z, z.Count)));
            }
        }
        else
        {
            int index = items.IndexOf(chip);
            menu.Items.Add(Entry(loc["Toolbar_MoveEarlier"], "", () => Move(items, index - 1), index > 0));
            menu.Items.Add(Entry(loc["Toolbar_MoveLater"], "", () => Move(items, index + 1), index < items.Count - 1));
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var (zone, name) in zones)
            {
                if (ReferenceEquals(zone, items)) continue;
                var z = zone;
                menu.Items.Add(Entry(string.Format(loc["Toolbar_MoveTo"], name), "", () => Move(z, z.Count)));
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Entry(loc["Toolbar_Remove"], "", () => Move(_tray, _tray.Count), chip.Id != "play"));
        }
        menu.ShowAt(anchor);
    }

    public void SetLayout(PlayerToolbarLayout layout)
    {
        _suppress = true;
        var loc = App.GetService<Services.ILocalizationService>();
        Chip Make(string id) => new() { Id = id, Glyph = Glyphs.TryGetValue(id, out var g) ? g : "", Label = loc["Toolbar_Item_" + id] };

        var used = new HashSet<string>();
        void Fill(ObservableCollection<Chip> target, IEnumerable<string> ids)
        {
            target.Clear();
            foreach (var id in ids)
            {
                if (!Glyphs.ContainsKey(id) || !used.Add(id)) continue;
                target.Add(Make(id));
            }
        }
        Fill(_left, layout.Left);
        Fill(_center, layout.Center);
        Fill(_right, layout.Right);
        _tray.Clear();
        foreach (var id in (AllIds.Length > 0 ? AllIds : Glyphs.Keys.ToArray()))
        {
            if (!used.Contains(id)) _tray.Add(Make(id));
        }
        _mode = layout.Mode;
        UpdateModeButtons();
        _suppress = false;
    }

    public PlayerToolbarLayout GetLayout() => new()
    {
        Mode = _mode,
        Left = _left.Select(c => c.Id).ToList(),
        Center = _center.Select(c => c.Id).ToList(),
        Right = _right.Select(c => c.Id).ToList()
    };

    private void Raise()
    {
        if (_suppress) return;
        // Play must stay on the bar
        if (!_left.Concat(_center).Concat(_right).Any(c => c.Id == "play"))
        {
            var play = _tray.FirstOrDefault(c => c.Id == "play");
            if (play != null) { _tray.Remove(play); _center.Insert(0, play); }
        }
        LayoutChanged?.Invoke(this, GetLayout());
    }
}
