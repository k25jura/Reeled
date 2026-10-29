using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;
using Reeled.ViewModels;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Reeled.Views;

/// <summary>
/// Browser-style tabs for the library. A WinUI <see cref="TabView"/> is used as a strip only (its tabs have
/// no content; the clips below belong to whichever tab is active). The strip is the window's title-bar row:
/// at the top of the content area, or above the horizontal bar when the navigation is at the top.
/// </summary>
public sealed partial class HomePage
{
    private const double TabStripHeight = 40;
    // The header's top padding normally leaves room for the caption buttons; the strip sits in that room
    private const double TabHeaderCaptionPadding = 32;
    private const double TabStripGap = 10;
    private const double TabStripPush = TabStripHeight + TabStripGap - TabHeaderCaptionPadding;

    private TabView _tabView = null!;
    private Grid _tabStripHolder = null!;
    private Panel? _tabStripHost;
    private Border _tabFooter = null!;
    private readonly TranslateTransform _tabStripTranslate = new();
    private readonly Dictionary<LibraryTab, TabViewItem> _tabItems = new();
    private bool _syncingTabs;
    private bool _tabDragging;
    private bool _tabSelectionCheckQueued;
    private bool _tabStripShown;
    private Storyboard? _tabStripStoryboard;

    // ---------------------------------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------------------------------

    private void WireTabs()
    {
        _tabFooter = new Border { MinWidth = 40, Background = new SolidColorBrush(Colors.Transparent) };
        _tabView = new TabView
        {
            TabWidthMode = TabViewWidthMode.Equal,
            CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.Auto,
            IsAddTabButtonVisible = true,
            CanReorderTabs = true,
            CanDragTabs = true,
            AllowDropTabs = true,
            VerticalAlignment = VerticalAlignment.Top,
            TabStripFooter = _tabFooter
        };
        // The strip lives in the title bar: no line under it like a document tab bar has
        _tabView.Resources["TabViewBorderBrush"] = new SolidColorBrush(Colors.Transparent);

        _tabView.SelectionChanged += OnTabStripSelectionChanged;
        _tabView.AddTabButtonClick += (s, e) => _ = OpenNewTabAsync();
        _tabView.TabCloseRequested += (s, e) =>
        {
            if (e.Tab?.Tag is LibraryTab tab) _ = CloseTabAsync(tab);
        };
        WireTabStripDrop();
        _tabView.TabDragStarting += (s, e) => _tabDragging = true;
        _tabView.TabDragCompleted += (s, e) => DispatcherQueue.TryEnqueue(OnTabDragSettled);
        _tabView.Loaded += OnTabViewLoaded;
        _tabView.SizeChanged += (s, e) => UpdateLibraryTitleBar();
        _tabFooter.SizeChanged += (s, e) => UpdateLibraryTitleBar();
        MainContentRoot.SizeChanged += (s, e) => UpdateLibraryTitleBar();

        _tabStripHolder = new Grid
        {
            Height = TabStripHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            RenderTransform = _tabStripTranslate
        };
        _tabStripHolder.Children.Add(_tabView);

        ViewModel.TabsChanged += ReconcileTabStrip;
        ViewModel.TabStateChanged += tab => DispatcherQueue.TryEnqueue(() =>
        {
            if (_tabItems.TryGetValue(tab, out var item)) RefreshTabItem(tab, item);
        });
        // Folder tabs show the folder's game icon once one is known
        ViewModel.GameIconsChanged += () => DispatcherQueue.TryEnqueue(RefreshTabsLocalization);

        // Middle-click on a section or folder opens it in a new background tab
        AttachMiddleClickToOpen(HomeNavButton, () => OpenSectionInNewTab(NavigationSection.Home));
        AttachMiddleClickToOpen(FavoritesNavButton, () => OpenSectionInNewTab(NavigationSection.Favorites));
        AttachMiddleClickToOpen(SavedMomentsNavButton, () => OpenSectionInNewTab(NavigationSection.SavedMoments));
        AttachMiddleClickToOpen(SharedNavButton, () => OpenSectionInNewTab(NavigationSection.Shared));
        DirectoriesTreeView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnFolderTreePointerPressed), handledEventsToo: true);

        ParentTabStrip();
        ReconcileTabStrip();
        RefreshTabsLocalization();
    }

    private void OnTabViewLoaded(object sender, RoutedEventArgs e)
    {
        // Tab shortcuts are handled by the page (see RegisterTabShortcuts); the strip's own would act twice
        _tabView.KeyboardAccelerators.Clear();
        UpdateTabStripTooltips();
        UpdateLibraryTitleBar();
    }

    private void UpdateTabStripTooltips()
    {
        var add = FindVisualChildByName<Button>(_tabView, "AddButton");
        if (add != null) ToolTipService.SetToolTip(add, LayoutLoc["Tabs_NewTab"] + " (Ctrl+T)");
    }

    /// <summary>Language changes and new game icons: titles, menus and tooltips are rebuilt.</summary>
    private void RefreshTabsLocalization()
    {
        foreach (var (tab, item) in _tabItems) RefreshTabItem(tab, item);
        UpdateTabStripTooltips();

        // The sidebar's section buttons carry the "open in new tab" entry too
        HomeNavButton.ContextFlyout = CreateSectionVisibilityMenu(NavigationSection.Home);
        FavoritesNavButton.ContextFlyout = CreateSectionVisibilityMenu(NavigationSection.Favorites);
        SavedMomentsNavButton.ContextFlyout = CreateSectionVisibilityMenu(NavigationSection.SavedMoments);
        SharedNavButton.ContextFlyout = CreateSectionVisibilityMenu(NavigationSection.Shared);
    }

    // ---------------------------------------------------------------------------------------------
    // Tab items
    // ---------------------------------------------------------------------------------------------

    private (string Title, string Glyph, ImageSource? Icon, string? Tip) DescribeTab(LibraryTab tab)
    {
        var loc = LayoutLoc;
        switch (tab.Section)
        {
            case NavigationSection.Favorites: return (loc["Nav_Favorites"], "", null, null);
            case NavigationSection.SavedMoments: return (loc["Nav_SavedMoments"], "", null, null);
            case NavigationSection.Shared: return (loc["Nav_Shared"], "", null, null);
            case NavigationSection.Folder:
                var node = tab.FolderPath != null ? HomeViewModel.FindNodeByPath(ViewModel.Directories, tab.FolderPath) : null;
                string name = node?.DisplayName ?? System.IO.Path.GetFileName((tab.FolderPath ?? string.Empty).TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = tab.FolderPath ?? loc["Nav_Home"];
                return (name, "", node?.GameIcon, tab.FolderPath);
            default: return (loc["Nav_Home"], "", null, null);
        }
    }

    private TabViewItem CreateTabItem(LibraryTab tab)
    {
        var item = new TabViewItem { Tag = tab };
        RefreshTabItem(tab, item);
        AttachTabDrop(item);
        return item;
    }

    /// <summary>Icon (the section's glyph, or the folder's game icon) and title for a tab; also its menu.</summary>
    private void RefreshTabItem(LibraryTab tab, TabViewItem item)
    {
        var (title, glyph, icon, tip) = DescribeTab(tab);

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement iconElement = icon != null
            ? CreateGameIconElement(icon, 16)
            : new FontIcon { Glyph = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(iconElement);
        var text = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        item.Header = header;
        ToolTipService.SetToolTip(item, tip ?? title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, title);
        item.ContextFlyout = CreateTabMenu(tab);
    }

    private MenuFlyout CreateTabMenu(LibraryTab tab)
    {
        var loc = LayoutLoc;
        var menu = new MenuFlyout();
        menu.Opening += OnFlyoutOpening;
        menu.Opened += OnFlyoutOpened;

        MenuFlyoutItem Item(string key, Action click, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = loc[key] };
            if (shortcut != null) item.KeyboardAcceleratorTextOverride = shortcut;
            item.Click += (s, e) => click();
            menu.Items.Add(item);
            return item;
        }

        Item("Tabs_NewTab", () => _ = OpenNewTabAsync(), "Ctrl+T");
        Item("Tabs_Duplicate", () => _ = DuplicateTabAsync(tab));
        menu.Items.Add(new MenuFlyoutSeparator());
        var close = Item("Tabs_Close", () => _ = CloseTabAsync(tab), "Ctrl+W");
        var others = Item("Tabs_CloseOthers", () => _ = CloseOtherTabsAsync(tab));
        var right = Item("Tabs_CloseRight", () => _ = CloseTabsToTheRightAsync(tab));

        menu.Opening += (s, e) =>
        {
            int count = ViewModel.Tabs.Count;
            int index = IndexOfTab(tab);
            close.IsEnabled = count > 1;
            others.IsEnabled = count > 1;
            right.IsEnabled = index >= 0 && index < count - 1;
        };
        return menu;
    }

    private int IndexOfTab(LibraryTab tab)
    {
        for (int i = 0; i < ViewModel.Tabs.Count; i++)
        {
            if (ReferenceEquals(ViewModel.Tabs[i], tab)) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------------------------------------
    // Keeping the strip in step with the view model
    // ---------------------------------------------------------------------------------------------

    private void ReconcileTabStrip()
    {
        _syncingTabs = true;
        try
        {
            var tabs = ViewModel.Tabs;
            foreach (var (tab, item) in _tabItems.ToList())
            {
                if (tabs.Contains(tab))
                {
                    continue;
                }
                _tabView.TabItems.Remove(item);
                _tabItems.Remove(tab);
            }

            for (int i = 0; i < tabs.Count; i++)
            {
                if (!_tabItems.TryGetValue(tabs[i], out var item))
                {
                    item = CreateTabItem(tabs[i]);
                    _tabItems[tabs[i]] = item;
                    _tabView.TabItems.Insert(i, item);
                }
                else if (i >= _tabView.TabItems.Count || !ReferenceEquals(_tabView.TabItems[i], item))
                {
                    _tabView.TabItems.Remove(item);
                    _tabView.TabItems.Insert(i, item);
                }
            }

            // The last tab can't be closed
            foreach (var item in _tabItems.Values) item.IsClosable = tabs.Count > 1;

            if (_tabItems.TryGetValue(ViewModel.ActiveTab, out var active) && !ReferenceEquals(_tabView.SelectedItem, active))
            {
                _tabView.SelectedItem = active;
            }
        }
        finally
        {
            _syncingTabs = false;
        }
        UpdateTabStripVisibility(animate: IsLoaded);
    }

    /// <summary>
    /// A click on a tab selects it. Drags remove and re-add the dragged tab, which briefly selects its
    /// neighbour, so the selection is only acted on once things have settled.
    /// </summary>
    private void OnTabStripSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs || _tabDragging || _tabSelectionCheckQueued) return;
        _tabSelectionCheckQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _tabSelectionCheckQueued = false;
            if (_syncingTabs || _tabDragging) return;
            if (_tabView.SelectedItem is TabViewItem { Tag: LibraryTab tab } && !ReferenceEquals(tab, ViewModel.ActiveTab))
            {
                _ = ActivateTabAsync(tab);
            }
        });
    }

    /// <summary>After a drag the strip's order is the new tab order.</summary>
    private void OnTabDragSettled()
    {
        _tabDragging = false;
        var order = _tabView.TabItems.OfType<TabViewItem>().Select(i => i.Tag).OfType<LibraryTab>().ToList();
        ViewModel.ReorderTabs(order);
        // Dragging may have moved the selection with it; the active tab is the one that stays selected
        ReconcileTabStrip();
    }

    // ---------------------------------------------------------------------------------------------
    // Switching, opening and closing
    // ---------------------------------------------------------------------------------------------

    private string CurrentViewKey() => $"{ViewModel.CurrentSection}|{ViewModel.SelectedDirectory?.FullPath}";

    private void SaveActiveTabScroll()
    {
        // While a restore is still settling, the tab's real position is the one being restored
        ViewModel.ActiveTab.ScrollOffset = ScrollRestoreActive ? _scrollRestoreTarget : ActiveScrollViewer()?.VerticalOffset ?? 0;
    }

    /// <summary>Makes a tab the active one: its view fades in and its scroll position and search come back.</summary>
    private async Task ActivateTabAsync(LibraryTab tab)
    {
        if (ReferenceEquals(tab, ViewModel.ActiveTab)) return;
        SaveActiveTabScroll();
        string keyBefore = CurrentViewKey();
        SelectTreeFolderFor(tab);
        BeginScrollRestore(tab.ScrollOffset);
        await ViewModel.ActivateTabAsync(tab);
        AfterTabShown(keyBefore);
    }

    /// <summary>The view model already points at another tab (one was closed): bring its view up.</summary>
    private async Task ShowActiveTabAsync(string keyBefore)
    {
        var tab = ViewModel.ActiveTab;
        SelectTreeFolderFor(tab);
        BeginScrollRestore(tab.ScrollOffset);
        await ViewModel.ApplyActiveTabAsync();
        AfterTabShown(keyBefore);
    }

    private void AfterTabShown(string keyBefore)
    {
        // Switching to another view plays the usual view transition; the same view (two tabs on Home with
        // different searches) gets the quick fade-in instead
        if (CurrentViewKey() == keyBefore) PlayContentEnter();

        if (!string.IsNullOrEmpty(ViewModel.SearchQuery)) SetSearchOpen(true);
        else if (_isSearchOpen) SetSearchOpen(false);

        SelectTreeFolderFor(ViewModel.ActiveTab);
        UpdateActiveIndicator(animate: true);
    }

    private void SelectTreeFolderFor(LibraryTab tab)
    {
        if (DirectoriesTreeView == null) return;
        DirectoriesTreeView.SelectedItem = tab.Section == NavigationSection.Folder && tab.FolderPath != null
            ? HomeViewModel.FindNodeByPath(ViewModel.Directories, tab.FolderPath)
            : null;
    }

    private async Task OpenNewTabAsync()
    {
        var tab = ViewModel.AddTab();
        await ActivateTabAsync(tab);
    }

    private async Task DuplicateTabAsync(LibraryTab source)
    {
        if (ReferenceEquals(source, ViewModel.ActiveTab)) SaveActiveTabScroll();
        var tab = ViewModel.DuplicateTab(source);
        await ActivateTabAsync(tab);
    }

    private async Task CloseTabAsync(LibraryTab tab)
    {
        if (ViewModel.Tabs.Count <= 1) return;
        string keyBefore = CurrentViewKey();
        if (ViewModel.CloseTab(tab) != null) await ShowActiveTabAsync(keyBefore);
    }

    private async Task CloseOtherTabsAsync(LibraryTab keep)
    {
        string keyBefore = CurrentViewKey();
        if (ViewModel.CloseTabsWhere((t, i) => true, keep) != null) await ShowActiveTabAsync(keyBefore);
    }

    private async Task CloseTabsToTheRightAsync(LibraryTab tab)
    {
        int index = IndexOfTab(tab);
        if (index < 0) return;
        string keyBefore = CurrentViewKey();
        if (ViewModel.CloseTabsWhere((t, i) => i > index, tab) != null) await ShowActiveTabAsync(keyBefore);
    }

    private void CycleTab(int step)
    {
        int count = ViewModel.Tabs.Count;
        if (count <= 1) return;
        int next = ((IndexOfTab(ViewModel.ActiveTab) + step) % count + count) % count;
        _ = ActivateTabAsync(ViewModel.Tabs[next]);
    }

    private bool GoToTab(int number)
    {
        // 1-8 pick that tab, 9 always the last one
        int index = number >= 9 ? ViewModel.Tabs.Count - 1 : number - 1;
        if (index < 0 || index >= ViewModel.Tabs.Count) return false;
        _ = ActivateTabAsync(ViewModel.Tabs[index]);
        return true;
    }

    /// <summary>Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+Shift+Tab and Ctrl+1..9.</summary>
    private void RegisterTabShortcuts(Action<VirtualKey, VirtualKeyModifiers, Func<bool>> add)
    {
        add(VirtualKey.T, VirtualKeyModifiers.Control, () =>
        {
            _ = OpenNewTabAsync();
            return true;
        });
        add(VirtualKey.W, VirtualKeyModifiers.Control, () =>
        {
            if (ViewModel.Tabs.Count <= 1) return false;
            _ = CloseTabAsync(ViewModel.ActiveTab);
            return true;
        });
        add(VirtualKey.Tab, VirtualKeyModifiers.Control, () =>
        {
            CycleTab(+1);
            return true;
        });
        add(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            CycleTab(-1);
            return true;
        });
        for (int n = 1; n <= 9; n++)
        {
            int number = n;
            add(VirtualKey.Number1 + (n - 1), VirtualKeyModifiers.Control, () => GoToTab(number));
            add(VirtualKey.NumberPad1 + (n - 1), VirtualKeyModifiers.Control, () => GoToTab(number));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Opening in a new tab (menus, middle-click, Ctrl+click)
    // ---------------------------------------------------------------------------------------------

    private void OpenFolderInNewTab(DirectoryNode node) => ViewModel.AddTab(NavigationSection.Folder, node.FullPath);

    private void OpenSectionInNewTab(NavigationSection section) => ViewModel.AddTab(section);

    private static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void AttachMiddleClickToOpen(UIElement element, Action openInNewTab)
    {
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
        {
            if (!e.GetCurrentPoint(element).Properties.IsMiddleButtonPressed) return;
            e.Handled = true;
            openInNewTab();
        }), handledEventsToo: true);
    }

    private void OnFolderTreePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(DirectoriesTreeView).Properties.IsMiddleButtonPressed) return;
        var row = e.OriginalSource is DependencyObject source ? FindParent<TreeViewItem>(source) : null;
        var node = (row?.DataContext as DirectoryNode) ?? (row?.Content as DirectoryNode);
        if (node == null) return;
        e.Handled = true;
        OpenFolderInNewTab(node);
    }

    private MenuFlyoutItem CreateOpenInNewTabItem(Action open)
    {
        var item = new MenuFlyoutItem { Text = LayoutLoc["Tabs_OpenInNewTab"], Icon = new FontIcon { Glyph = "" } };
        item.Click += (s, e) => open();
        return item;
    }

    private MenuFlyoutItem CreateOpenInNewTabItem(DirectoryNode node) => CreateOpenInNewTabItem(() => OpenFolderInNewTab(node));

    private MenuFlyout CreateOpenInNewTabMenu(DirectoryNode node)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateOpenInNewTabItem(node));
        return menu;
    }

    // ---------------------------------------------------------------------------------------------
    // Scroll position of the tab being shown
    // ---------------------------------------------------------------------------------------------

    private double _scrollRestoreTarget;
    private long _scrollRestoreUntil;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _scrollRestoreTimer;

    private bool ScrollRestoreActive => _scrollRestoreTarget > 0 && Environment.TickCount64 < _scrollRestoreUntil;

    /// <summary>
    /// Puts the clips back at a tab's scroll offset. The new clips arrive and lay out over a few frames
    /// (and "scroll to top" requests come with them), so the offset is held for a moment.
    /// </summary>
    private void BeginScrollRestore(double offset)
    {
        _scrollRestoreTarget = offset;
        if (offset <= 0)
        {
            _scrollRestoreTimer?.Stop();
            return;
        }

        _scrollRestoreUntil = Environment.TickCount64 + 800;
        if (_scrollRestoreTimer == null)
        {
            _scrollRestoreTimer = DispatcherQueue.CreateTimer();
            _scrollRestoreTimer.Interval = TimeSpan.FromMilliseconds(50);
            _scrollRestoreTimer.IsRepeating = true;
            _scrollRestoreTimer.Tick += (s, e) => ApplyScrollRestore();
        }
        _scrollRestoreTimer.Start();
    }

    private void ApplyScrollRestore()
    {
        if (!ScrollRestoreActive)
        {
            _scrollRestoreTarget = 0;
            _scrollRestoreTimer?.Stop();
            return;
        }

        var viewer = ActiveScrollViewer();
        if (viewer == null || ViewModel.ShowSkeletonLoading) return;
        bool lastChance = Environment.TickCount64 > _scrollRestoreUntil - 100;
        // Wait until the content is tall enough (it lays out over a few frames); settle for less at the end
        if (viewer.ScrollableHeight + 1 < _scrollRestoreTarget && !lastChance) return;
        double target = Math.Min(_scrollRestoreTarget, viewer.ScrollableHeight);
        if (Math.Abs(viewer.VerticalOffset - target) > 1) viewer.ChangeView(null, target, null, disableAnimation: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Where the strip sits and when it shows
    // ---------------------------------------------------------------------------------------------

    private void UpdateTabStripVisibility(bool animate)
    {
        bool want = ViewModel.Tabs.Count >= 2 || LayoutStorage.CurrentSettings.AlwaysShowTabs;
        if (want == _tabStripShown) return;
        SetTabStripShown(want, animate);
    }

    /// <summary>Puts the strip in the host that fits the layout and its space and visuals in their resting state.</summary>
    private void PlaceTabStrip()
    {
        _tabStripStoryboard?.Stop();
        _tabStripStoryboard = null;
        ParentTabStrip();
        ApplyTabStripFinalState();
    }

    private void ParentTabStrip()
    {
        Panel host = _sidebarPosition == SidebarPosition.Top ? TabStripHostTop : TabStripOverlayMain;
        // Parent isn't reliable before the page is in the tree, so remember the host ourselves
        if (ReferenceEquals(_tabStripHost, host)) return;
        _tabStripHost?.Children.Remove(_tabStripHolder);
        if (_tabStripHolder.Parent is Panel old) old.Children.Remove(_tabStripHolder);
        host.Children.Add(_tabStripHolder);
        _tabStripHost = host;
    }

    private void ApplyTabStripFinalState()
    {
        bool top = _sidebarPosition == SidebarPosition.Top;
        TabStripHostTop.Height = top && _tabStripShown ? TabStripHeight : 0;
        TabSpacerMain.Height = !top && _tabStripShown ? TabStripPush : 0;
        _tabStripHolder.Visibility = _tabStripShown ? Visibility.Visible : Visibility.Collapsed;
        _tabStripHolder.Opacity = _tabStripShown ? 1 : 0;
        _tabStripTranslate.Y = 0;
        // With the strip up there, the caption buttons are over the strip rather than over the bar
        HorizontalNavCaptionSpacer.Width = top && !_tabStripShown ? 140 / Helpers.UiZoom.Current : 0;
    }

    /// <summary>
    /// The strip slides down from the top edge and fades in while the content opens room for it; hiding
    /// reverses that. Only the spacer's height is animated in layout, so the clips reflow just vertically.
    /// </summary>
    private void SetTabStripShown(bool shown, bool animate)
    {
        bool top = _sidebarPosition == SidebarPosition.Top;
        FrameworkElement spacer = top ? TabStripHostTop : TabSpacerMain;
        // Where things are now, so an interrupted animation continues from there
        bool wasVisible = _tabStripHolder.Visibility == Visibility.Visible;
        double fromHeight = spacer.Height;
        double fromOpacity = wasVisible ? _tabStripHolder.Opacity : 0;
        double fromY = wasVisible ? _tabStripTranslate.Y : -14;
        double fromCaption = HorizontalNavCaptionSpacer.Width;

        _tabStripStoryboard?.Stop();
        _tabStripStoryboard = null;
        _tabStripShown = shown;
        ParentTabStrip();

        if (!animate || Helpers.Motion.IsReduced || !IsLoaded)
        {
            ApplyTabStripFinalState();
            UpdateLibraryTitleBar();
            return;
        }

        (top ? TabSpacerMain : (FrameworkElement)TabStripHostTop).Height = 0;
        _tabStripHolder.Visibility = Visibility.Visible;
        double captionWidth = 140 / Helpers.UiZoom.Current;
        var sb = new Storyboard();
        if (shown)
        {
            sb.Children.Add(Helpers.Motion.Spline(spacer, "Height", top ? TabStripHeight : TabStripPush, Helpers.Motion.Long, from: fromHeight, dependent: true));
            sb.Children.Add(Helpers.Motion.Spline(_tabStripHolder, "Opacity", 1, Helpers.Motion.Medium, from: fromOpacity, delayMs: 40));
            sb.Children.Add(Helpers.Motion.Spline(_tabStripTranslate, "Y", 0, Helpers.Motion.Long, from: fromY));
            if (top) sb.Children.Add(Helpers.Motion.Spline(HorizontalNavCaptionSpacer, "Width", 0, Helpers.Motion.Long, from: fromCaption, dependent: true));
        }
        else
        {
            sb.Children.Add(Helpers.Motion.Spline(_tabStripHolder, "Opacity", 0, Helpers.Motion.Short, from: fromOpacity, accelerate: true));
            sb.Children.Add(Helpers.Motion.Spline(_tabStripTranslate, "Y", -14, Helpers.Motion.Short, from: fromY, accelerate: true));
            sb.Children.Add(Helpers.Motion.Spline(spacer, "Height", 0, Helpers.Motion.Medium, from: fromHeight, delayMs: 80, dependent: true));
            if (top) sb.Children.Add(Helpers.Motion.Spline(HorizontalNavCaptionSpacer, "Width", captionWidth, Helpers.Motion.Medium, from: fromCaption, delayMs: 80, dependent: true));
        }
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_tabStripStoryboard, sb)) return;
            _tabStripStoryboard = null;
            sb.Stop();
            ApplyTabStripFinalState();
            UpdateLibraryTitleBar();
        };
        _tabStripStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
        UpdateLibraryTitleBar();
    }

    /// <summary>The top host is only as tall as the strip is open; clip the strip to it so it slides in from the edge.</summary>
    private void OnTabStripHostTopSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TabStripHostTop.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    /// <summary>
    /// The strip keeps clear of the caption buttons. In the right-hand layout the sidebar (with the caption
    /// buttons over its top) is beside the strip, so nothing needs to be left free there.
    /// </summary>
    private void UpdateTabStripInsets()
    {
        double caption = 140 / Helpers.UiZoom.Current;
        double right = caption;
        if (_sidebarPosition == SidebarPosition.Right && SidebarSpacer.Width > 0)
        {
            right = Math.Max(0, caption - SidebarSpacer.Width);
        }
        _tabStripHolder.Margin = new Thickness(8, 0, right, 0);
    }

    /// <summary>
    /// Where the title bar starts to drag: just right of the last tab and its add button, where the strip's
    /// footer is. Everything left of that stays clickable.
    /// </summary>
    private double TabStripDragLeft(MainWindow mw)
    {
        var root = mw.ContentRoot;
        try
        {
            if (_tabFooter.XamlRoot == null || _tabFooter.ActualWidth <= 0) return root.ActualWidth;
            return Math.Max(0, _tabFooter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).X);
        }
        catch
        {
            // Not laid out yet: leave the strip fully clickable until it is
            return root.ActualWidth;
        }
    }
}
