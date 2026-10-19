using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Reeled.Helpers;
using Reeled.Models;

namespace Reeled.ViewModels;

/// <summary>
/// One tab of the library: where it is (a section, or a folder), what is typed in its search box and how
/// far its clips were scrolled. Titles and icons are derived by the view from the section and folder.
/// </summary>
public sealed class LibraryTab
{
    public NavigationSection Section { get; set; } = NavigationSection.Home;
    public string? FolderPath { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    /// <summary>Vertical scroll offset of the clips, kept while another tab is in front.</summary>
    public double ScrollOffset { get; set; }

    public LibraryTab Clone() => new()
    {
        Section = Section,
        FolderPath = FolderPath,
        SearchQuery = SearchQuery,
        ScrollOffset = ScrollOffset
    };
}

/// <summary>
/// Browser-style tabs for the library. Whatever the user navigates to (sidebar, bar, folder chips, search)
/// is written into the active tab; switching tabs puts the tab's state back through the normal navigation
/// methods. The open tabs are saved with the settings and reopened on start.
/// </summary>
public partial class HomeViewModel
{
    private readonly List<LibraryTab> _tabs = new();
    private bool _applyingTab;
    private bool _tabSyncQueued;
    private bool _suppressFilter;
    private bool _tabsRestorePending;
    private DispatcherQueueTimer? _tabSaveTimer;

    public IReadOnlyList<LibraryTab> Tabs => _tabs;
    public LibraryTab ActiveTab { get; private set; } = new();

    /// <summary>Tabs were added, removed, moved or another one became active.</summary>
    public event Action? TabsChanged;

    /// <summary>A tab now shows something else, so its title and icon need refreshing.</summary>
    public event Action<LibraryTab>? TabStateChanged;

    private static string SectionName(NavigationSection section) => section.ToString();

    private static bool TryParseSection(string? name, out NavigationSection section) =>
        Enum.TryParse(name, ignoreCase: true, out section) && Enum.IsDefined(section);

    /// <summary>Builds the tabs from the saved ones (folders that are gone are dropped) or starts with one.</summary>
    private void InitializeTabs()
    {
        var settings = _storageService.CurrentSettings;
        LibraryTab? active = null;
        if (settings.ReopenTabs)
        {
            for (int i = 0; i < settings.OpenTabs.Count; i++)
            {
                var state = settings.OpenTabs[i];
                if (!TryParseSection(state.Section, out var section)) continue;
                if (section == NavigationSection.Folder && !IsKnownFolder(state.FolderPath, settings)) continue;
                var tab = new LibraryTab
                {
                    Section = section,
                    FolderPath = section == NavigationSection.Folder ? state.FolderPath : null,
                    SearchQuery = state.SearchQuery ?? string.Empty
                };
                _tabs.Add(tab);
                // A dropped active tab hands over to the one before it
                if (i <= settings.ActiveTabIndex) active = tab;
            }
        }

        if (_tabs.Count == 0)
        {
            _tabs.Add(new LibraryTab());
            active = _tabs[0];
        }
        ActiveTab = active ?? _tabs[0];
        _tabsRestorePending = true;
    }

    private static bool IsKnownFolder(string? path, AppSettings settings) =>
        !string.IsNullOrEmpty(path) && Directory.Exists(path) &&
        settings.WatchDirectories.Any(root => PathUtil.IsUnder(path, root));

    /// <summary>The first sync after start brings the restored active tab's view up.</summary>
    private async Task<bool> RestoreActiveTabOnFirstSyncAsync()
    {
        if (!_tabsRestorePending) return false;
        _tabsRestorePending = false;
        // Nothing has been shown yet, so even a view that looks the same still has to be built
        await ApplyActiveTabAsync(force: true);
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Keeping the active tab in step with the view
    // ---------------------------------------------------------------------------------------------

    partial void OnCurrentSectionChanged(NavigationSection value) => QueueTabSync();

    /// <summary>
    /// Section and folder change one after the other, so the tab is updated once the navigation that
    /// changed them has finished.
    /// </summary>
    private void QueueTabSync()
    {
        if (_applyingTab || _tabSyncQueued) return;
        _tabSyncQueued = true;
        _dispatcherQueue.TryEnqueue(() =>
        {
            _tabSyncQueued = false;
            SyncActiveTabFromView();
        });
    }

    private void SyncActiveTabFromView()
    {
        if (_applyingTab) return;
        var tab = ActiveTab;
        string? path = CurrentSection == NavigationSection.Folder ? SelectedDirectory?.FullPath : null;
        if (CurrentSection == NavigationSection.Folder && path == null) return;

        bool moved = tab.Section != CurrentSection || !PathUtil.PathEquals(tab.FolderPath, path);
        bool searched = !string.Equals(tab.SearchQuery, SearchQuery, StringComparison.Ordinal);
        if (!moved && !searched) return;

        tab.Section = CurrentSection;
        tab.FolderPath = path;
        tab.SearchQuery = SearchQuery;
        if (moved) tab.ScrollOffset = 0;
        PersistTabs();
        if (moved) TabStateChanged?.Invoke(tab);
    }

    /// <summary>Puts the active tab's state on screen: its search, then its section or folder.</summary>
    public async Task ApplyActiveTabAsync(bool force = false)
    {
        var tab = ActiveTab;
        _applyingTab = true;
        try
        {
            string search = tab.SearchQuery;
            bool searchChanged = !string.Equals(SearchQuery, search, StringComparison.Ordinal);
            if (searchChanged)
            {
                // The view switch below filters once; filtering the old view first would only flash
                _suppressFilter = true;
                SearchQuery = search;
                _suppressFilter = false;
            }

            DirectoryNode? node = null;
            if (tab.Section == NavigationSection.Folder)
            {
                node = tab.FolderPath != null ? FindNodeByPath(Directories, tab.FolderPath) : null;
                if (node == null)
                {
                    // The folder is gone (or not loaded): the tab falls back to Home
                    tab.Section = NavigationSection.Home;
                    tab.FolderPath = null;
                    TabStateChanged?.Invoke(tab);
                }
            }

            bool sameView = !force && tab.Section == CurrentSection &&
                            (tab.Section != NavigationSection.Folder || ReferenceEquals(SelectedDirectory, node));
            if (sameView)
            {
                if (searchChanged) ApplyFilterAndSort();
            }
            else
            {
                switch (tab.Section)
                {
                    case NavigationSection.Favorites: await SelectFavoritesAsync(); break;
                    case NavigationSection.SavedMoments: await SelectSavedMomentsAsync(); break;
                    case NavigationSection.Folder: await SelectDirectoryAsync(node!); break;
                    default: await SelectHomeAsync(); break;
                }
            }
        }
        finally
        {
            _applyingTab = false;
        }
        PersistTabs();
    }

    // ---------------------------------------------------------------------------------------------
    // Opening, activating, closing and moving tabs
    // ---------------------------------------------------------------------------------------------

    public LibraryTab AddTab(NavigationSection section = NavigationSection.Home, string? folderPath = null, int? index = null)
    {
        var tab = new LibraryTab { Section = section, FolderPath = section == NavigationSection.Folder ? folderPath : null };
        InsertTab(tab, index);
        return tab;
    }

    /// <summary>A copy of a tab, right after it (its scroll position comes along).</summary>
    public LibraryTab DuplicateTab(LibraryTab source)
    {
        var tab = source.Clone();
        InsertTab(tab, _tabs.IndexOf(source) + 1);
        return tab;
    }

    private void InsertTab(LibraryTab tab, int? index)
    {
        int at = index is int i ? Math.Clamp(i, 0, _tabs.Count) : _tabs.Count;
        _tabs.Insert(at, tab);
        PersistTabs();
        TabsChanged?.Invoke();
    }

    public async Task ActivateTabAsync(LibraryTab tab)
    {
        if (ReferenceEquals(tab, ActiveTab) || !_tabs.Contains(tab)) return;
        // Whatever the user just did belongs to the tab they are leaving
        SyncActiveTabFromView();
        ActiveTab = tab;
        TabsChanged?.Invoke();
        await ApplyActiveTabAsync();
    }

    /// <summary>
    /// Closes a tab (never the last one). When it was the active tab its neighbour becomes active and is
    /// returned, so the view can be switched to it.
    /// </summary>
    public LibraryTab? CloseTab(LibraryTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0 || _tabs.Count <= 1) return null;
        bool wasActive = ReferenceEquals(tab, ActiveTab);
        _tabs.RemoveAt(index);
        LibraryTab? next = null;
        if (wasActive)
        {
            next = _tabs[Math.Min(index, _tabs.Count - 1)];
            ActiveTab = next;
        }
        PersistTabs();
        TabsChanged?.Invoke();
        return next;
    }

    /// <summary>Closes the tabs picked by <paramref name="predicate"/>; returns the tab that became active, if it changed.</summary>
    public LibraryTab? CloseTabsWhere(Func<LibraryTab, int, bool> predicate, LibraryTab keep)
    {
        bool activeClosed = false;
        for (int i = _tabs.Count - 1; i >= 0; i--)
        {
            var tab = _tabs[i];
            if (ReferenceEquals(tab, keep) || !predicate(tab, i)) continue;
            if (ReferenceEquals(tab, ActiveTab)) activeClosed = true;
            _tabs.RemoveAt(i);
        }
        if (activeClosed) ActiveTab = keep;
        PersistTabs();
        TabsChanged?.Invoke();
        return activeClosed ? keep : null;
    }

    /// <summary>Takes over the order of the tab strip after a drag.</summary>
    public void ReorderTabs(IList<LibraryTab> order)
    {
        if (order.Count != _tabs.Count || order.Any(t => !_tabs.Contains(t))) return;
        if (order.SequenceEqual(_tabs)) return;
        _tabs.Clear();
        _tabs.AddRange(order);
        PersistTabs();
        TabsChanged?.Invoke();
    }

    // ---------------------------------------------------------------------------------------------
    // Saving
    // ---------------------------------------------------------------------------------------------

    /// <summary>Copies the tabs into the settings (so a close always saves the latest) and writes them out soon.</summary>
    public void PersistTabs()
    {
        var settings = _storageService.CurrentSettings;
        if (settings.ReopenTabs)
        {
            settings.OpenTabs = _tabs.Select(t => new LibraryTabState
            {
                Section = SectionName(t.Section),
                FolderPath = t.FolderPath,
                SearchQuery = string.IsNullOrEmpty(t.SearchQuery) ? null : t.SearchQuery
            }).ToList();
            settings.ActiveTabIndex = Math.Max(0, _tabs.IndexOf(ActiveTab));
        }
        else
        {
            settings.OpenTabs = new List<LibraryTabState>();
            settings.ActiveTabIndex = 0;
        }

        // Typing in the search box shouldn't write the settings file on every key
        _tabSaveTimer ??= CreateTabSaveTimer();
        _tabSaveTimer.Stop();
        _tabSaveTimer.Start();
    }

    private DispatcherQueueTimer CreateTabSaveTimer()
    {
        var timer = _dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(600);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => _ = _storageService.SaveSettingsAsync(_storageService.CurrentSettings);
        return timer;
    }
}
