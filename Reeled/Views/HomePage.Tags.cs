using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

/// <summary>
/// Tags in the library: the header filter (tags and folder tags), the "Tags" submenu in a clip's menu and the
/// tag manager. Chips on cards and rows bind to the clip's own tags.
/// </summary>
public sealed partial class HomePage
{
    private ElementTheme DialogTheme => ActualTheme != ElementTheme.Default ? ActualTheme : App.CurrentTheme;

    private void WireTags()
    {
        // Built once the button is in the window: creating the flyout's text box from the page constructor
        // intermittently failed inside XAML at startup
        TagsFilterButton.Loaded += (s, e) => EnsureTagsPicker();
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

    private Controls.SearchPicker? _tagsPicker;

    /// <summary>The filter is a searchable picker: a library can have dozens of folder tags.</summary>
    private void EnsureTagsPicker()
    {
        if (_tagsPicker != null) return;
        _tagsPicker = new Controls.SearchPicker();
        _tagsPicker.Flyout.Opening += (s, e) => BuildTagsFilterPicker();
        TagsFilterButton.Flyout = _tagsPicker.Flyout;
    }

    /// <summary>Built each time it opens so counts and new tags are always current.</summary>
    private void BuildTagsFilterPicker()
    {
        if (_tagsPicker == null) return;
        var loc = LayoutLoc;
        _tagsPicker.SetTexts(loc["Picker_Search"], loc["Picker_NoMatches"]);
        _tagsPicker.Configure(DialogTheme, FlyoutPlacementMode.BottomEdgeAlignedRight);

        var entries = new List<Controls.PickerEntry>();
        var tags = ViewModel.TagLibrary.ToList();
        entries.Add(new Controls.PickerEntry { Text = loc["Tags_YourTags"], IsHeader = true });
        if (tags.Count == 0)
        {
            entries.Add(new Controls.PickerEntry { Text = loc["Tags_FilterEmpty"], IsEnabled = false });
        }
        foreach (var tag in tags)
        {
            string name = tag;
            entries.Add(new Controls.PickerEntry
            {
                Text = name,
                Glyph = ViewModel.TagGlyph(name),
                Trailing = ViewModel.CountWithTag(name).ToString(),
                IsChecked = ViewModel.ActiveTagFilters.Contains(name, StringComparer.CurrentCultureIgnoreCase),
                Invoked = e =>
                {
                    ViewModel.SetTagFilter(name, e.IsChecked == true);
                    _tagsPicker?.SetFooterEnabled(0, ViewModel.ActiveFilterCount > 0);
                }
            });
        }

        var folders = ViewModel.GamesWithCounts();
        if (folders.Count > 0)
        {
            entries.Add(new Controls.PickerEntry { Text = loc["Tags_Folders"], IsHeader = true });
            foreach (var (folder, count) in folders)
            {
                string name = folder;
                var icon = ViewModel.FolderIcon(name);
                entries.Add(new Controls.PickerEntry
                {
                    Text = name,
                    Image = icon,
                    Glyph = icon == null ? "" : null,
                    Trailing = count.ToString(),
                    IsChecked = ViewModel.ActiveGameFilters.Contains(name, StringComparer.CurrentCultureIgnoreCase),
                    Invoked = e =>
                    {
                        ViewModel.SetGameFilter(name, e.IsChecked == true);
                        _tagsPicker?.SetFooterEnabled(0, ViewModel.ActiveFilterCount > 0);
                    }
                });
            }
        }

        var footer = new List<Controls.PickerEntry>
        {
            new()
            {
                Text = loc["Tags_ClearFilters"],
                Glyph = "",
                IsEnabled = ViewModel.ActiveFilterCount > 0,
                Invoked = _ => ViewModel.ClearFilters()
            },
            new()
            {
                Text = loc["Tags_Manage"],
                Glyph = "",
                Invoked = async _ => await Helpers.TagUi.ShowManageDialogAsync(ViewModel, XamlRoot, DialogTheme)
            }
        };
        _tagsPicker.SetEntries(entries, footer);
    }

    /// <summary>The "Tags" submenu of a clip's context menu.</summary>
    private MenuFlyoutSubItem CreateClipTagsMenu(GameClip clip) =>
        Helpers.TagUi.BuildClipSubmenu(ViewModel, clip, XamlRoot, DialogTheme, "ContextMenu_Tags");
}
