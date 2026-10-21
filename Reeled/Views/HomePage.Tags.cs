using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

/// <summary>
/// Tags in the library: the header filter (tags and games), the "Tags" submenu in a clip's menu and the
/// tag manager. Chips on cards and rows bind to the clip's own tags.
/// </summary>
public sealed partial class HomePage
{
    private ElementTheme DialogTheme => ActualTheme != ElementTheme.Default ? ActualTheme : App.CurrentTheme;

    private void WireTags()
    {
        ViewModel.TagsChanged += () => DispatcherQueue.TryEnqueue(UpdateTagsFilterButton);
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.ActiveFilterCount)) UpdateTagsFilterButton();
        };
    }

    /// <summary>The button reads "Tags", or "Tags · 2" in the accent colour while filters are active.</summary>
    private void UpdateTagsFilterButton()
    {
        if (TagsFilterText == null || TagsFilterIcon == null) return;
        var loc = LayoutLoc;
        int active = ViewModel.ActiveFilterCount;
        TagsFilterText.Text = active > 0 ? string.Format(loc["Tags_ButtonActive"], active) : loc["Tags_Button"];
        if (active > 0)
        {
            var accent = GetThemeResourceBrush("AccentTextFillColorPrimaryBrush");
            TagsFilterText.Foreground = accent;
            TagsFilterIcon.Foreground = accent;
        }
        else
        {
            TagsFilterText.ClearValue(TextBlock.ForegroundProperty);
            TagsFilterIcon.ClearValue(IconElement.ForegroundProperty);
        }
    }

    private void OnTagsFilterOpening(object? sender, object e)
    {
        BuildTagsFilterMenu();
        OnFlyoutOpening(sender, e);
    }

    /// <summary>Built each time it opens so counts and new tags are always current.</summary>
    private void BuildTagsFilterMenu()
    {
        var loc = LayoutLoc;
        var items = TagsFilterFlyout.Items;
        items.Clear();

        var tags = ViewModel.TagLibrary.ToList();
        if (tags.Count == 0)
        {
            items.Add(new MenuFlyoutItem { Text = loc["Tags_FilterEmpty"], IsEnabled = false });
        }
        foreach (var tag in tags)
        {
            string name = tag;
            var item = new ToggleMenuFlyoutItem
            {
                Text = name,
                // Shown at the trailing edge, where shortcuts normally sit
                KeyboardAcceleratorTextOverride = ViewModel.CountWithTag(name).ToString(),
                IsChecked = ViewModel.ActiveTagFilters.Contains(name, StringComparer.CurrentCultureIgnoreCase)
            };
            item.Click += (s, args) => ViewModel.SetTagFilter(name, item.IsChecked);
            items.Add(item);
        }

        var games = ViewModel.GamesWithCounts();
        if (games.Count > 0)
        {
            items.Add(new MenuFlyoutSeparator());
            items.Add(new MenuFlyoutItem { Text = loc["Tags_Games"], IsEnabled = false });
            foreach (var (game, count) in games)
            {
                string name = game;
                var item = new ToggleMenuFlyoutItem
                {
                    Text = name,
                    KeyboardAcceleratorTextOverride = count.ToString(),
                    IsChecked = ViewModel.ActiveGameFilters.Contains(name, StringComparer.CurrentCultureIgnoreCase)
                };
                item.Click += (s, args) => ViewModel.SetGameFilter(name, item.IsChecked);
                items.Add(item);
            }
        }

        items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem
        {
            Text = loc["Tags_ClearFilters"],
            Icon = new FontIcon { Glyph = "" },
            IsEnabled = ViewModel.ActiveFilterCount > 0
        };
        clear.Click += (s, args) => ViewModel.ClearFilters();
        items.Add(clear);

        var manage = new MenuFlyoutItem { Text = loc["Tags_Manage"], Icon = new FontIcon { Glyph = "" } };
        manage.Click += async (s, args) => await Helpers.TagUi.ShowManageDialogAsync(ViewModel, XamlRoot, DialogTheme);
        items.Add(manage);
    }

    /// <summary>The "Tags" submenu of a clip's context menu.</summary>
    private MenuFlyoutSubItem CreateClipTagsMenu(GameClip clip) =>
        Helpers.TagUi.BuildClipSubmenu(ViewModel, clip, XamlRoot, DialogTheme, "ContextMenu_Tags");
}
