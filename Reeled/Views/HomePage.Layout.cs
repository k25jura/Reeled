using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

/// <summary>
/// Library layout: which edge the navigation sits on (left / right sidebar, or a horizontal bar at the
/// top or bottom), which navigation sections are shown, and where the footer buttons live.
/// </summary>
public sealed partial class HomePage
{
    private SidebarPosition _sidebarPosition = SidebarPosition.Left;
    private bool _layoutApplied;

    private const double TopBarHeight = 48;
    private const double BottomBarHeight = 52;

    private bool IsHorizontalNav => _sidebarPosition is SidebarPosition.Top or SidebarPosition.Bottom;
    private bool IsSidebarRight => _sidebarPosition == SidebarPosition.Right;

    private static Services.ILocalStorageService LayoutStorage => App.GetService<Services.ILocalStorageService>();
    private static Services.ILocalizationService LayoutLoc => App.GetService<Services.ILocalizationService>();

    /// <summary>Applies the stored layout; cheap when nothing changed (the page is cached across visits).</summary>
    private void ApplyLayoutSettings()
    {
        var settings = LayoutStorage.CurrentSettings;
        if (!_layoutApplied || settings.SidebarPosition != _sidebarPosition)
        {
            ApplySidebarPosition(settings.SidebarPosition);
        }
        ApplyNavSectionVisibility();
        UpdateTabStripVisibility(animate: false);
        if (!_layoutApplied || settings.LibraryViewMode != _libraryViewMode)
        {
            _libraryViewMode = settings.LibraryViewMode == LibraryViewMode.List ? LibraryViewMode.Grid : LibraryViewMode.List;
            ApplyLibraryViewMode(settings.LibraryViewMode, animate: false);
        }
        _layoutApplied = true;
    }

    /// <summary>Changes the navigation edge from inside the library, with a soft crossfade.</summary>
    private void SetSidebarPosition(SidebarPosition position)
    {
        var settings = LayoutStorage.CurrentSettings;
        if (settings.SidebarPosition == position) return;
        settings.SidebarPosition = position;
        _ = LayoutStorage.SaveSettingsAsync(settings);

        if (Helpers.Motion.IsReduced)
        {
            ApplySidebarPosition(position);
            return;
        }

        var fadeOut = new Storyboard();
        fadeOut.Children.Add(Helpers.Motion.Spline(MainRootGrid, "Opacity", 0, Helpers.Motion.Short - 60, accelerate: true));
        fadeOut.Completed += (s, e) =>
        {
            ApplySidebarPosition(position);
            var fadeIn = new Storyboard();
            fadeIn.Children.Add(Helpers.Motion.Spline(MainRootGrid, "Opacity", 1, Helpers.Motion.Medium, from: 0));
            Helpers.Motion.BeginExact(fadeIn);
        };
        Helpers.Motion.BeginExact(fadeOut);
    }

    private void ApplySidebarPosition(SidebarPosition position)
    {
        _sidebarPosition = position;
        bool right = IsSidebarRight;
        bool horizontal = IsHorizontalNav;
        bool top = position == SidebarPosition.Top;

        _sidebarStoryboard?.Stop();
        _sidebarStoryboard = null;

        // Columns: the spacer reserving the sidebar's width sits on the sidebar's side
        MainRootGrid.ColumnDefinitions[0].Width = right ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        MainRootGrid.ColumnDefinitions[1].Width = right ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SidebarSpacer, right ? 1 : 0);
        Grid.SetColumn(MainContentRoot, right ? 0 : 1);

        SidebarContainer.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        SidebarContentBorder.HorizontalAlignment = SidebarContainer.HorizontalAlignment;
        SidebarContentBorder.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        SidebarResizeHandle.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SidebarResizeHandle.Margin = right ? new Thickness(-5, 0, 0, 0) : new Thickness(0, 0, -5, 0);
        // On the right, the sidebar's header drops below the caption buttons
        SidebarHeaderRow.Margin = right ? new Thickness(14, 42, 14, 10) : new Thickness(14, 10, 14, 10);

        // The "show sidebar" button sits on the sidebar's side of the header
        Grid.SetColumn(ShowSidebarContainer, right ? 3 : 0);
        ShowSidebarContainer.Margin = right ? new Thickness(-12, 0, 0, 0) : new Thickness(0, 0, -12, 0);
        ShowSidebarButton.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        SidebarTranslate.X = 0;
        MainContentTranslate.X = 0;
        HeaderToolbarTranslate.X = 0;

        if (horizontal)
        {
            SidebarContainer.Visibility = Visibility.Collapsed;
            SidebarSpacer.Width = 0;
            ShowSidebarContainer.Visibility = Visibility.Collapsed;
            ShowSidebarContainer.Width = 0;
            (App.Window as MainWindow)?.SetSidebarOpen(false);

            HorizontalNavBar.Visibility = Visibility.Visible;
            HorizontalNavBar.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            HorizontalNavBar.Height = top ? TopBarHeight : BottomBarHeight;
            HorizontalNavBar.BorderThickness = top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
            MainContentRoot.Margin = top ? new Thickness(0, TopBarHeight, 0, 0) : new Thickness(0, 0, 0, BottomBarHeight);
            LibraryHeaderBorder.Padding = top ? new Thickness(24, 20, 24, 10) : new Thickness(24, 32, 24, 10);
            MoveFooter(toHorizontal: true);
            BuildHorizontalNav();
        }
        else
        {
            HorizontalNavBar.Visibility = Visibility.Collapsed;
            MainContentRoot.Margin = new Thickness(0);
            LibraryHeaderBorder.Padding = new Thickness(24, 32, 24, 10);
            MoveFooter(toHorizontal: false);
            AnimateSidebar(ViewModel.IsSidebarOpen, animate: false);
        }

        // The tab strip moves with the navigation: above the bar when it is at the top, else over the content
        PlaceTabStrip();
        // The floating selection bar / progress card clears the bottom bar
        FileOpHost.Margin = new Thickness(16, 0, 16, position == SidebarPosition.Bottom ? BottomBarHeight + 16 : 24);

        _lastCalculatedWidth = -1;
        _lastColumnCount = -1;
        UpdateGridResponsiveLayout();
        UpdateLibraryTitleBar();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            UpdateActiveIndicator(animate: false);
            UpdateLibraryTitleBar();
        });
        // Whatever the underline was under no longer applies after the switch: place it afresh
        QueueHorizontalIndicator(animate: false, snap: true);
    }

    /// <summary>The settings / update / feedback / refresh row lives in the sidebar or the bar.</summary>
    private void MoveFooter(bool toHorizontal)
    {
        var grid = SidebarFooterGrid;
        if (toHorizontal && HorizontalNavFooterHost.Child == grid) return;
        if (!toHorizontal && SidebarFooterBorder.Child == grid) return;

        if (grid.Parent is Border oldHost) oldHost.Child = null;
        if (toHorizontal)
        {
            HorizontalNavFooterHost.Child = grid;
            SidebarFooterBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarFooterBorder.Child = grid;
            SidebarFooterBorder.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Tells the window where the library's drag region is. With the bar at the top it is the title bar:
    /// the empty parts drag the window and the items stay clickable.
    /// </summary>
    private void UpdateLibraryTitleBar()
    {
        if (App.Window is not MainWindow mw || !IsLoaded) return;
        if (_tabStripShown)
        {
            // The tab strip is the title bar: the empty stretch right of the tabs drags the window and the
            // tabs and their buttons stay clickable
            UpdateTabStripInsets();
            mw.SetLibraryTitleBar(TabStripDragLeft(mw), 0, TabStripHeight);
            return;
        }
        switch (_sidebarPosition)
        {
            case SidebarPosition.Top:
                if (_tabStripStoryboard == null) HorizontalNavCaptionSpacer.Width = 140 / Helpers.UiZoom.Current;
                var (left, right) = TopBarDragGap(mw);
                mw.SetLibraryTitleBar(left, right, TopBarHeight);
                break;
            case SidebarPosition.Right:
                mw.SetLibraryTitleBar(0, 0, 36);
                break;
            default:
                mw.SetLibraryTitleBar(48, 0, 36);
                break;
        }
    }

    /// <summary>The empty stretch between the bar's items and its buttons, which drags the window.</summary>
    private (double Left, double Right) TopBarDragGap(MainWindow mw)
    {
        try
        {
            var root = mw.ContentRoot;
            double used = Math.Min(HorizontalNavScroller.ActualWidth, HorizontalNavItems.ActualWidth);
            double left = HorizontalNavScroller.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(used, 0)).X;
            double footerLeft = HorizontalNavFooterHost.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
            double right = Math.Max(0, root.ActualWidth - footerLeft - 140 / Helpers.UiZoom.Current);
            return (Math.Max(0, left), right);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Items scroll before they swallow the whole bar, so there is always room to drag.</summary>
    private void LimitHorizontalNavWidth()
    {
        if (!IsHorizontalNav) return;
        double reserved = HorizontalNavLogo.ActualWidth + 32 + HorizontalNavFooterHost.ActualWidth + HorizontalNavCaptionSpacer.Width;
        double max = HorizontalNavBar.ActualWidth - reserved - (_sidebarPosition == SidebarPosition.Top ? 72 : 0);
        HorizontalNavScroller.MaxWidth = Math.Max(120, max);
        HorizontalNavScroller.HorizontalAlignment = HorizontalAlignment.Left;
    }

    private void OnHorizontalNavLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(sender, HorizontalNavBar)) LimitHorizontalNavWidth();
        if (_sidebarPosition == SidebarPosition.Top) UpdateLibraryTitleBar();
        QueueHorizontalIndicator(animate: false);
    }

    private void OnHorizontalNavWheel(object sender, PointerRoutedEventArgs e)
    {
        // A vertical wheel scrolls the bar sideways when it overflows
        int delta = e.GetCurrentPoint(HorizontalNavScroller).Properties.MouseWheelDelta;
        if (HorizontalNavScroller.ScrollableWidth <= 0 || delta == 0) return;
        HorizontalNavScroller.ChangeView(HorizontalNavScroller.HorizontalOffset - delta, null, null);
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Horizontal navigation items
    // ---------------------------------------------------------------------------------------------

    private readonly List<(FrameworkElement Element, Func<bool> IsSelected, TextBlock Label, FontIcon Icon)> _hNavEntries = new();
    private bool _hNavIndicatorShown;

    private void BuildHorizontalNav(bool snapIndicator = true)
    {
        if (!IsHorizontalNav || HorizontalNavItems == null) return;
        var loc = LayoutLoc;
        HorizontalNavItems.Children.Clear();
        _hNavEntries.Clear();

        AddHorizontalTab("", loc["Nav_Home"], ViewModel.AllClipsCount, () => ViewModel.IsHomeSelected,
            () => OnHomeTabClick(this, new RoutedEventArgs()));
        if (IsNavSectionShown(NavigationSection.Favorites))
        {
            AddHorizontalTab("", loc["Nav_Favorites"], ViewModel.FavoritesCount, () => ViewModel.IsFavoritesSelected,
                () => OnFavoritesTabClick(this, new RoutedEventArgs()), NavigationSection.Favorites);
        }
        if (IsNavSectionShown(NavigationSection.SavedMoments))
        {
            AddHorizontalTab("", loc["Nav_SavedMoments"], ViewModel.SavedMomentsCount, () => ViewModel.IsSavedMomentsSelected,
                () => OnSavedMomentsTabClick(this, new RoutedEventArgs()), NavigationSection.SavedMoments);
        }

        if (ViewModel.Directories.Count > 0)
        {
            HorizontalNavItems.Children.Add(new Border
            {
                Width = 1,
                Height = 20,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Helpers.ThemeBrushes.Get("DividerStrokeColorDefaultBrush", this)
            });
            foreach (var root in ViewModel.Directories) AddHorizontalFolder(root);
        }

        var add = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE8F4", FontSize = 12 }
        };
        ToolTipService.SetToolTip(add, loc["Tooltip_AddWatchFolder"]);
        add.Click += OnAddFolderClick;
        HorizontalNavItems.Children.Add(add);

        UpdateHorizontalNavVisuals();
        // The items are new elements with no size yet; the indicator is placed once they have been laid out
        if (snapIndicator) QueueHorizontalIndicator(animate: false);
    }

    private Button CreateHorizontalItemButton(string glyph, string label, int count, out TextBlock labelText, out FontIcon icon, ImageSource? gameIcon = null)
    {
        icon = new FontIcon { Glyph = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        labelText = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        // A folder with a game icon shows that instead of the glyph (the glyph element stays for the tint logic)
        panel.Children.Add(gameIcon != null ? CreateGameIconElement(gameIcon, 18) : icon);
        panel.Children.Add(labelText);
        if (count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11.5,
                Foreground = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", this),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        return new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(12, 7, 12, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Content = panel
        };
    }

    private void AddHorizontalTab(string glyph, string label, int count, Func<bool> isSelected, Action invoke, NavigationSection? section = null)
    {
        var button = CreateHorizontalItemButton(glyph, label, count, out var text, out var icon);
        NavigationSection target = section ?? NavigationSection.Home;
        // Ctrl+click and middle-click open the section in a new background tab
        button.Click += (s, e) =>
        {
            if (IsCtrlDown()) OpenSectionInNewTab(target);
            else invoke();
        };
        AttachMiddleClickToOpen(button, () => OpenSectionInNewTab(target));
        button.ContextFlyout = CreateSectionVisibilityMenu(target);
        _hNavEntries.Add((button, isSelected, text, icon));
        HorizontalNavItems.Children.Add(button);
    }

    private void AddHorizontalFolder(DirectoryNode root)
    {
        bool InThisRoot() => ViewModel.CurrentSection == NavigationSection.Folder && ViewModel.SelectedDirectory != null &&
                             (ViewModel.SelectedDirectory == root || IsDescendantPath(ViewModel.SelectedDirectory.FullPath, root.FullPath));

        string label = root.DisplayName;
        int count = root.ClipCount;
        if (InThisRoot() && ViewModel.SelectedDirectory != root)
        {
            label = $"{root.DisplayName}  ›  {ViewModel.SelectedDirectory!.DisplayName}";
            count = ViewModel.SelectedDirectory.ClipCount;
        }

        var button = CreateHorizontalItemButton("", label, count, out var text, out var icon, root.GameIcon);
        button.Click += async (s, e) =>
        {
            if (IsCtrlDown())
            {
                OpenFolderInNewTab(root);
                return;
            }
            DirectoriesTreeView.SelectedItem = null;
            await ViewModel.SelectDirectoryAsync(root);
            ScrollClipsToTop();
        };
        AttachMiddleClickToOpen(button, () => OpenFolderInNewTab(root));
        button.ContextFlyout = CreateHorizontalFolderMenu(root);

        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(button);
        bool hasSubfolders = root.HasSubDirectories || root.SubDirectories.Count > 0;

        void ShowSubfolderMenu()
        {
            if (_openSubfolderMenu?.IsOpen == true) return;
            var picker = new Controls.SearchPicker();
            picker.SetTexts(LayoutLoc["Picker_Search"], LayoutLoc["Picker_NoMatches"]);
            picker.Configure(DialogTheme, _sidebarPosition == SidebarPosition.Bottom ? FlyoutPlacementMode.TopEdgeAlignedLeft : FlyoutPlacementMode.BottomEdgeAlignedLeft);
            picker.SetEntries(SubfolderEntries(root));
            var menu = picker.Flyout;
            _openSubfolderMenu = menu;
            TrackFolderDropdown(menu);
            menu.Closed += (s, e) =>
            {
                if (ReferenceEquals(_openSubfolderMenu, menu)) _openSubfolderMenu = null;
            };
            picker.ShowAt(button);
        }

        // Clips can be dropped on a folder chip; resting on one opens its subfolders to drop into
        WireFolderDropTarget(button, () => root, hasSubfolders ? () => ArmDwell(button, ShowSubfolderMenu) : null);
        if (hasSubfolders)
        {
            var chevron = new Button
            {
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Padding = new Thickness(5, 9, 5, 9),
                Margin = new Thickness(-4, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon { Glyph = "", FontSize = 9, Foreground = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", this) }
            };
            ToolTipService.SetToolTip(chevron, LayoutLoc["Nav_Subfolders"]);
            chevron.Click += (s, e) => ShowSubfolderMenu();
            WireFolderDropTarget(chevron, () => root, () => ArmDwell(button, ShowSubfolderMenu));
            group.Children.Add(chevron);
        }
        _hNavEntries.Add((button, InThisRoot, text, icon));
        HorizontalNavItems.Children.Add(group);
    }

    private static bool IsDescendantPath(string path, string rootPath) =>
        path.StartsWith(rootPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A watch folder's subfolders for its dropdown: the folder itself first, then the whole tree indented,
    /// each with its game icon, clip count, folder menu and drop target. Searching flattens it and shows
    /// where each match lives.
    /// </summary>
    private List<Controls.PickerEntry> SubfolderEntries(DirectoryNode root)
    {
        var entries = new List<Controls.PickerEntry>();
        bool IsOpen(DirectoryNode n) => ViewModel.SelectedDirectory == n && ViewModel.CurrentSection == NavigationSection.Folder;

        Controls.PickerEntry Entry(DirectoryNode node, string text, int depth, string? detail) => new()
        {
            Text = text,
            Detail = detail,
            Depth = depth,
            Image = node.GameIcon,
            Glyph = node.GameIcon == null ? "" : null,
            Trailing = node.ClipCount > 0 ? node.ClipCount.ToString() : null,
            IsCurrent = IsOpen(node),
            Invoked = async _ =>
            {
                await ViewModel.SelectDirectoryAsync(node);
                ScrollClipsToTop();
            },
            Prepare = row =>
            {
                row.ContextFlyout = CreateFolderRowMenu(node);
                WireFolderDropTarget(row, () => node);
            }
        };

        void AddChildren(DirectoryNode parent, int depth, string path)
        {
            foreach (var child in parent.SubDirectories)
            {
                entries.Add(Entry(child, child.DisplayName, depth, path));
                AddChildren(child, depth + 1, $"{path}  ›  {child.DisplayName}");
            }
        }

        entries.Add(Entry(root, string.Format(LayoutLoc["Nav_AllOfFolder"], root.DisplayName), 0, null));
        AddChildren(root, 0, root.DisplayName);
        return entries;
    }

    /// <summary>A folder row's menu in the dropdown: open in a new tab, game icon, file commands.</summary>
    private MenuFlyout CreateFolderRowMenu(DirectoryNode node)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateOpenInNewTabItem(node));
        AddGameIconMenu(menu.Items, node);
        AddFolderFileMenuItems(menu.Items, node);
        return menu;
    }

    private MenuFlyout CreateHorizontalFolderMenu(DirectoryNode root)
    {
        var loc = LayoutLoc;
        var menu = new MenuFlyout();
        menu.Items.Add(CreateOpenInNewTabItem(root));
        menu.Items.Add(new MenuFlyoutSeparator());
        var open = new MenuFlyoutItem { Text = loc["FolderMenu_RevealExplorer"], Icon = new FontIcon { Glyph = "" } };
        open.Click += (s, e) => ViewModel.OpenFolderInExplorer(root);
        var copy = new MenuFlyoutItem { Text = loc["FolderMenu_CopyPath"], Icon = new FontIcon { Glyph = "" } };
        copy.Click += (s, e) => ViewModel.CopyFolderPath(root);
        menu.Items.Add(open);
        menu.Items.Add(copy);
        AddFolderFileMenuItems(menu.Items, root);
        AddGameIconMenu(menu.Items, root);
        return menu;
    }

    private void UpdateHorizontalNavVisuals()
    {
        if (!IsHorizontalNav) return;
        var primary = Helpers.ThemeBrushes.Get("TextFillColorPrimaryBrush", this);
        var secondary = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", this);
        foreach (var (_, isSelected, label, icon) in _hNavEntries)
        {
            bool selected = isSelected();
            label.Foreground = selected ? primary : secondary;
            icon.Foreground = selected ? primary : secondary;
            label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    // The accent underline slides to the selected item (Windows 11 top navigation style). Its position is a
    // composition translation: a XAML storyboard that held its end value kept overriding later snaps, which
    // left the underline misaligned after the bar was rebuilt. Updates are queued and run once the items
    // have been laid out, so a freshly rebuilt tab is never measured while it is still 0 wide.
    private Microsoft.UI.Composition.Visual? _hNavIndicatorVisual;
    private float _hNavIndicatorX;
    private bool _hNavIndicatorQueued;
    private bool _hNavQueuedAnimate;
    private bool _hNavQueuedSnap;
    private bool _hNavWaitingForLayout;
    private long _hNavSlideUntil;

    private Microsoft.UI.Composition.Visual HNavIndicatorVisual()
    {
        if (_hNavIndicatorVisual == null)
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(HorizontalNavIndicator, true);
            _hNavIndicatorVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(HorizontalNavIndicator);
            _hNavIndicatorVisual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        }
        return _hNavIndicatorVisual;
    }

    /// <summary>
    /// Asks for the underline to be moved under the selected item after the next layout. Requests made in
    /// the same turn merge: a slide wins over a plain re-measure, a snap (position switch) wins over both.
    /// </summary>
    private void QueueHorizontalIndicator(bool animate, bool snap = false)
    {
        _hNavQueuedAnimate |= animate;
        _hNavQueuedSnap |= snap;
        if (_hNavIndicatorQueued) return;
        _hNavIndicatorQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _hNavIndicatorQueued = false;
            // A synchronous layout pass settles the rebuilt items (not allowed from inside a layout event)
            try { HorizontalNavItemsHost.UpdateLayout(); } catch { }
            bool slide = _hNavQueuedAnimate && !_hNavQueuedSnap;
            bool force = _hNavQueuedSnap;
            _hNavQueuedAnimate = false;
            _hNavQueuedSnap = false;
            UpdateHorizontalIndicator(slide, force);
        });
    }

    private void UpdateHorizontalIndicator(bool animate, bool forceSnap = false)
    {
        if (!IsHorizontalNav || HorizontalNavIndicator == null) return;
        if (HorizontalNavBar.Visibility != Visibility.Visible) return;
        var visual = HNavIndicatorVisual();

        var entry = _hNavEntries.FirstOrDefault(e => e.IsSelected());
        if (entry.Element == null)
        {
            // No tab is selected: let the underline fade away where it is
            if (_hNavIndicatorShown)
            {
                _hNavIndicatorShown = false;
                FadeHorizontalIndicator(show: false);
            }
            return;
        }

        Windows.Foundation.Point pos;
        if (entry.Element.ActualWidth <= 0 || HorizontalNavItemsHost.ActualWidth <= 0)
        {
            WaitForHorizontalNavLayout(animate, forceSnap);
            return;
        }
        try
        {
            pos = entry.Element.TransformToVisual(HorizontalNavItemsHost).TransformPoint(new Windows.Foundation.Point(0, 0));
        }
        catch
        {
            // Not in the tree yet
            WaitForHorizontalNavLayout(animate, forceSnap);
            return;
        }

        // Items that shift while the underline is still sliding re-aim the slide instead of cutting it short
        if (!forceSnap && Environment.TickCount64 < _hNavSlideUntil) animate = true;
        float x = (float)(pos.X + (entry.Element.ActualWidth - HorizontalNavIndicator.Width) / 2);
        bool moved = !_hNavIndicatorShown || Math.Abs(x - _hNavIndicatorX) > 0.5f;
        _hNavIndicatorX = x;

        if (!_hNavIndicatorShown || forceSnap || !animate || Helpers.Motion.IsReduced)
        {
            if (moved || forceSnap)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(x, 0, 0));
            }
            if (!_hNavIndicatorShown)
            {
                _hNavIndicatorShown = true;
                FadeHorizontalIndicator(show: true);
            }
        }
        else if (moved)
        {
            // Slide from wherever it is now (also while an earlier slide is still running)
            var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, new System.Numerics.Vector3(x, 0, 0), Helpers.Motion.DecelerateCurve(visual.Compositor));
            slide.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Medium);
            visual.StartAnimation("Translation", slide);
            _hNavSlideUntil = Environment.TickCount64 + (long)Helpers.Motion.Medium;
        }

        // Keep the selected item in view when the bar overflows
        double left = pos.X, right = pos.X + entry.Element.ActualWidth;
        if (left < HorizontalNavScroller.HorizontalOffset || right > HorizontalNavScroller.HorizontalOffset + HorizontalNavScroller.ViewportWidth)
        {
            HorizontalNavScroller.ChangeView(Math.Max(0, left - 24), null, null, disableAnimation: !animate);
        }
    }

    /// <summary>The target isn't measured yet: try again on the next layout pass instead of guessing.</summary>
    private void WaitForHorizontalNavLayout(bool animate, bool snap)
    {
        _hNavQueuedAnimate |= animate;
        _hNavQueuedSnap |= snap;
        if (_hNavWaitingForLayout) return;
        _hNavWaitingForLayout = true;
        void OnLayout(object? s, object e)
        {
            var entry = _hNavEntries.FirstOrDefault(x => x.IsSelected());
            if (entry.Element != null && entry.Element.ActualWidth <= 0 && HorizontalNavBar.Visibility == Visibility.Visible && IsHorizontalNav) return;
            HorizontalNavItemsHost.LayoutUpdated -= OnLayout;
            _hNavWaitingForLayout = false;
            QueueHorizontalIndicator(animate: false);
        }
        HorizontalNavItemsHost.LayoutUpdated += OnLayout;
    }

    private void FadeHorizontalIndicator(bool show)
    {
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(HorizontalNavIndicator, "Opacity", show ? 1 : 0,
            show ? Helpers.Motion.Medium : Helpers.Motion.Short, accelerate: !show));
        Helpers.Motion.BeginExact(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Section visibility (Favorites, Saved Moments)
    // ---------------------------------------------------------------------------------------------

    private bool IsNavSectionShown(NavigationSection section)
    {
        var settings = LayoutStorage.CurrentSettings;
        var mode = section == NavigationSection.Favorites ? settings.FavoritesNavVisibility : settings.MomentsNavVisibility;
        int count = section == NavigationSection.Favorites ? ViewModel.FavoritesCount : ViewModel.SavedMomentsCount;
        return mode switch
        {
            NavSectionVisibility.Hidden => false,
            // Never pull the rug while the section is open (e.g. un-favoriting the last clip)
            NavSectionVisibility.WhenNotEmpty => count > 0 || ViewModel.CurrentSection == section,
            _ => true
        };
    }

    private void ApplyNavSectionVisibility()
    {
        bool favorites = IsNavSectionShown(NavigationSection.Favorites);
        bool moments = IsNavSectionShown(NavigationSection.SavedMoments);
        bool changed = (FavoritesNavButton.Visibility == Visibility.Visible) != favorites ||
                       (SavedMomentsNavButton.Visibility == Visibility.Visible) != moments;

        FavoritesNavButton.Visibility = favorites ? Visibility.Visible : Visibility.Collapsed;
        SavedMomentsNavButton.Visibility = moments ? Visibility.Visible : Visibility.Collapsed;

        if ((ViewModel.IsFavoritesSelected && !favorites) || (ViewModel.IsSavedMomentsSelected && !moments))
        {
            OnHomeTabClick(this, new RoutedEventArgs());
        }

        if (changed)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => UpdateActiveIndicator(animate: false));
            BuildHorizontalNav();
        }
    }

    private MenuFlyout CreateSectionVisibilityMenu(NavigationSection section)
    {
        var loc = LayoutLoc;
        var menu = new MenuFlyout();
        menu.Items.Add(CreateOpenInNewTabItem(() => OpenSectionInNewTab(section)));
        // Home can't be hidden, so it has nothing but the tab entry
        if (section == NavigationSection.Home) return menu;
        menu.Items.Add(new MenuFlyoutSeparator());
        var options = new[]
        {
            (NavSectionVisibility.Always, loc["Nav_ShowAlways"]),
            (NavSectionVisibility.WhenNotEmpty, loc["Nav_ShowWhenNotEmpty"]),
            (NavSectionVisibility.Hidden, loc["Nav_HideSection"]),
        };
        var radios = new List<(RadioMenuFlyoutItem, NavSectionVisibility)>();
        foreach (var (mode, text) in options)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "NavVis" + section };
            item.Click += (s, e) => SetNavSectionVisibility(section, mode);
            radios.Add((item, mode));
            menu.Items.Add(item);
        }
        menu.Opening += (s, e) =>
        {
            var settings = LayoutStorage.CurrentSettings;
            var current = section == NavigationSection.Favorites ? settings.FavoritesNavVisibility : settings.MomentsNavVisibility;
            foreach (var (item, mode) in radios) item.IsChecked = mode == current;
        };
        return menu;
    }

    private void SetNavSectionVisibility(NavigationSection section, NavSectionVisibility mode)
    {
        var settings = LayoutStorage.CurrentSettings;
        if (section == NavigationSection.Favorites) settings.FavoritesNavVisibility = mode;
        else settings.MomentsNavVisibility = mode;
        _ = LayoutStorage.SaveSettingsAsync(settings);
        ApplyNavSectionVisibility();
    }

    private void WireLayoutHandlers()
    {
        HorizontalNavItems.SizeChanged += OnHorizontalNavLayoutChanged;
        HorizontalNavScroller.SizeChanged += OnHorizontalNavLayoutChanged;
        HorizontalNavFooterHost.SizeChanged += OnHorizontalNavLayoutChanged;
        HorizontalNavBar.SizeChanged += OnHorizontalNavLayoutChanged;

        ViewModel.Directories.CollectionChanged += (s, e) => BuildHorizontalNav();
        ViewModel.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(HomeViewModel.CurrentSection):
                case nameof(HomeViewModel.SelectedDirectory):
                    if (IsHorizontalNav)
                    {
                        // Folder labels show the selected subfolder, so rebuild those; tabs just restyle
                        BuildHorizontalNav(snapIndicator: false);
                        QueueHorizontalIndicator(animate: true);
                    }
                    ApplyNavSectionVisibility();
                    break;
                case nameof(HomeViewModel.AllClipsCount):
                case nameof(HomeViewModel.FavoritesCount):
                case nameof(HomeViewModel.SavedMomentsCount):
                    ApplyNavSectionVisibility();
                    BuildHorizontalNav();
                    break;
            }
        };

        if (App.Window is MainWindow mw)
        {
            mw.ZoomLevelChanged += (s, z) => UpdateLibraryTitleBar();
        }

        AddNavigationPositionMenu();
        AddViewModeMenu();
    }

    /// <summary>View menu: where the navigation sits, so it's discoverable without opening Settings.</summary>
    private void AddNavigationPositionMenu()
    {
        var loc = LayoutLoc;
        var sub = new MenuFlyoutSubItem { Text = loc["Nav_Position"], Icon = new FontIcon { Glyph = "" } };
        var items = new List<(RadioMenuFlyoutItem, SidebarPosition)>();
        foreach (var (pos, key) in new[]
        {
            (SidebarPosition.Left, "Nav_PositionLeft"), (SidebarPosition.Right, "Nav_PositionRight"),
            (SidebarPosition.Top, "Nav_PositionTop"), (SidebarPosition.Bottom, "Nav_PositionBottom"),
        })
        {
            var item = new RadioMenuFlyoutItem { Text = loc[key], GroupName = "NavPosition" };
            item.Click += (s, e) => SetSidebarPosition(pos);
            items.Add((item, pos));
            sub.Items.Add(item);
        }
        ViewMenuFlyout.Items.Add(new MenuFlyoutSeparator());
        ViewMenuFlyout.Items.Add(sub);
        ViewMenuFlyout.Opening += (s, e) =>
        {
            foreach (var (item, pos) in items) item.IsChecked = pos == _sidebarPosition;
        };
        LayoutLoc.LanguageChanged += (s, e) => DispatcherQueue.TryEnqueue(() =>
        {
            sub.Text = LayoutLoc["Nav_Position"];
            foreach (var (item, pos) in items)
            {
                item.Text = LayoutLoc[pos switch
                {
                    SidebarPosition.Right => "Nav_PositionRight",
                    SidebarPosition.Top => "Nav_PositionTop",
                    SidebarPosition.Bottom => "Nav_PositionBottom",
                    _ => "Nav_PositionLeft"
                }];
            }
        });
    }
}
