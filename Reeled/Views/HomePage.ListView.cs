using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>
/// The library's details list: an alternative to the card grid with sortable column headers. Folder and
/// date columns fold into a subtitle under the name when there isn't room for them.
/// </summary>
public sealed partial class HomePage
{
    private LibraryViewMode _libraryViewMode = LibraryViewMode.Grid;
    private readonly HashSet<Grid> _listRows = new();
    private bool _listCompact;
    private ScrollViewer? _listScrollViewer;
    private ScrollViewer? _autoscrollViewer;

    private bool IsListMode => _libraryViewMode == LibraryViewMode.List;

    private ListViewBase ActiveClipsView => IsListMode ? ClipsListView : ClipsGridView;

    private ScrollViewer? ActiveScrollViewer()
    {
        if (IsListMode)
        {
            _listScrollViewer ??= FindVisualChild<ScrollViewer>(ClipsListView);
            return _listScrollViewer;
        }
        EnsureScrollViewerHooked();
        return _clipsScrollViewer;
    }

    /// <summary>Hands the clips to whichever view is showing; the hidden one holds nothing.</summary>
    private void SetClipsItemsSource(object? source)
    {
        if (IsListMode)
        {
            ClipsGridView.ItemsSource = null;
            ClipsListView.ItemsSource = source;
        }
        else
        {
            ClipsListView.ItemsSource = null;
            ClipsGridView.ItemsSource = source;
        }
    }

    private void SetLibraryViewMode(LibraryViewMode mode)
    {
        var settings = LayoutStorage.CurrentSettings;
        if (settings.LibraryViewMode != mode)
        {
            settings.LibraryViewMode = mode;
            _ = LayoutStorage.SaveSettingsAsync(settings);
        }
        ApplyLibraryViewMode(mode, animate: true);
    }

    private void ApplyLibraryViewMode(LibraryViewMode mode, bool animate)
    {
        if (mode == _libraryViewMode && _layoutApplied) return;
        _libraryViewMode = mode;

        ClipsListHost.Visibility = IsListMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateGridItemsSource();
        UpdateListColumns(force: true);
        UpdateListSortIndicators();
        UpdateViewModeMenu();

        if (animate && !Helpers.Motion.IsReduced)
        {
            // One soft fade-up for the whole view, like other view changes
            var sb = new Storyboard();
            sb.Children.Add(Helpers.Motion.Spline(ClipsHost, "Opacity", 1, Helpers.Motion.Medium, from: 0));
            sb.Children.Add(Helpers.Motion.Spline(ClipsHostTranslate, "Y", 0, Helpers.Motion.Long, from: 10));
            Helpers.Motion.BeginExact(sb);
        }
        ScrollClipsToTop();
    }

    // ---------------------------------------------------------------------------------------------
    // Columns
    // ---------------------------------------------------------------------------------------------

    private void OnClipsListSizeChanged(object sender, SizeChangedEventArgs e) => UpdateListColumns();

    private void UpdateListColumns(bool force = false)
    {
        if (!IsListMode || ClipsListView == null) return;
        bool compact = ClipsListView.ActualWidth > 0 && ClipsListView.ActualWidth < 760;
        if (compact == _listCompact && !force) return;
        _listCompact = compact;

        ApplyColumns(ListHeaderRow);
        ListHeaderFolderText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ListSortDate.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var row in _listRows) ApplyRowColumns(row);
    }

    private void ApplyColumns(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 6) return;
        grid.ColumnDefinitions[2].Width = new GridLength(_listCompact ? 0 : 180);
        grid.ColumnDefinitions[3].Width = new GridLength(_listCompact ? 0 : 150);
        // Collapsed columns shouldn't leave their spacing behind
        grid.ColumnSpacing = 14;
    }

    private void ApplyRowColumns(Grid row)
    {
        ApplyColumns(row);
        if (row.FindName("RowSubtitle") is UIElement subtitle) subtitle.Visibility = _listCompact ? Visibility.Visible : Visibility.Collapsed;
        if (row.FindName("RowFolder") is UIElement folder) folder.Visibility = _listCompact ? Visibility.Collapsed : Visibility.Visible;
        if (row.FindName("RowDate") is UIElement date) date.Visibility = _listCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnClipRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row && _listRows.Add(row)) ApplyRowColumns(row);
    }

    private void OnClipRowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row) _listRows.Remove(row);
    }

    private void OnClipRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement row) _hoveredClip = row.DataContext as GameClip;
    }

    private void OnClipRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement row && ReferenceEquals(_hoveredClip, row.DataContext)) _hoveredClip = null;
    }

    // ---------------------------------------------------------------------------------------------
    // Sorting from the column headers (maps onto the existing sort options)
    // ---------------------------------------------------------------------------------------------

    private void OnListHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not string column) return;
        int current = ViewModel.SortIndex;
        ViewModel.SortIndex = column switch
        {
            "name" => current == 2 ? 3 : 2,
            "date" => current == 0 ? 1 : 0,
            // Longest / largest first, then the other way round
            "duration" => current == 4 ? 6 : 4,
            "size" => current == 5 ? 7 : 5,
            _ => current
        };
        UpdateListSortIndicators();
    }

    private void UpdateListSortIndicators()
    {
        if (ListSortNameGlyph == null) return;
        int sort = ViewModel.SortIndex;
        void Set(FontIcon glyph, bool active, bool descending)
        {
            glyph.Opacity = active ? 1 : 0;
            glyph.Glyph = descending ? "" : "";
        }
        Set(ListSortNameGlyph, sort is 2 or 3, sort == 3);
        Set(ListSortDateGlyph, sort is 0 or 1, sort == 0);
        Set(ListSortDurationGlyph, sort is 4 or 6, sort == 4);
        Set(ListSortSizeGlyph, sort is 5 or 7, sort == 5);
    }

    private void LocalizeListHeaders()
    {
        var loc = LayoutLoc;
        if (ListHeaderNameText == null) return;
        ListHeaderNameText.Text = loc["List_Name"];
        ListHeaderFolderText.Text = loc["List_Folder"];
        ListHeaderDateText.Text = loc["List_Date"];
        ListHeaderDurationText.Text = loc["List_Length"];
        ListHeaderSizeText.Text = loc["List_Size"];
    }

    // ---------------------------------------------------------------------------------------------
    // View menu: Cards / List
    // ---------------------------------------------------------------------------------------------

    private RadioMenuFlyoutItem? _viewModeGridItem;
    private RadioMenuFlyoutItem? _viewModeListItem;

    private void AddViewModeMenu()
    {
        var loc = LayoutLoc;
        _viewModeGridItem = new RadioMenuFlyoutItem { Text = loc["View_Cards"], GroupName = "LibraryViewMode", Icon = new FontIcon { Glyph = "" } };
        _viewModeListItem = new RadioMenuFlyoutItem { Text = loc["View_List"], GroupName = "LibraryViewMode", Icon = new FontIcon { Glyph = "" } };
        _viewModeGridItem.Click += (s, e) => SetLibraryViewMode(LibraryViewMode.Grid);
        _viewModeListItem.Click += (s, e) => SetLibraryViewMode(LibraryViewMode.List);
        var header = new MenuFlyoutItem { Text = loc["View_Layout"], IsEnabled = false };
        ViewMenuFlyout.Items.Insert(0, header);
        ViewMenuFlyout.Items.Insert(1, _viewModeGridItem);
        ViewMenuFlyout.Items.Insert(2, _viewModeListItem);
        ViewMenuFlyout.Items.Insert(3, new MenuFlyoutSeparator());
        ViewMenuFlyout.Opening += (s, e) => UpdateViewModeMenu();
        LayoutLoc.LanguageChanged += (s, e) => DispatcherQueue.TryEnqueue(() =>
        {
            header.Text = LayoutLoc["View_Layout"];
            _viewModeGridItem.Text = LayoutLoc["View_Cards"];
            _viewModeListItem.Text = LayoutLoc["View_List"];
            LocalizeListHeaders();
        });
        LocalizeListHeaders();

        ClipsListView.SizeChanged += OnClipsListSizeChanged;
        ClipsListView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnClipsGridPointerPressed), handledEventsToo: true);
        ClipsListView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnClipsGridPointerMoved), handledEventsToo: true);
        ClipsListView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnClipsGridPointerReleased), handledEventsToo: true);
    }

    private void UpdateViewModeMenu()
    {
        if (_viewModeGridItem == null || _viewModeListItem == null) return;
        _viewModeGridItem.IsChecked = !IsListMode;
        _viewModeListItem.IsChecked = IsListMode;
        // Card size only applies to the grid
        ViewDensityHeader.Visibility = IsListMode ? Visibility.Collapsed : Visibility.Visible;
        ViewDensityComfortableItem.Visibility = ViewDensityHeader.Visibility;
        ViewDensityCompactItem.Visibility = ViewDensityHeader.Visibility;
        ViewDensityLargeItem.Visibility = ViewDensityHeader.Visibility;
        ViewMetadataHoverItem.Visibility = ViewDensityHeader.Visibility;
        ViewDensitySeparator.Visibility = ViewDensityHeader.Visibility;
        ViewMetadataSeparator.Visibility = ViewDensityHeader.Visibility;
    }
}
