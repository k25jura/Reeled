using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Models;
using Reeled.Services;
using Reeled.ViewModels;

namespace Reeled.Helpers;

/// <summary>
/// Tag menus and dialogs shared by the library and the player: the "Tags" submenu for a clip, the
/// "New tag" prompt and the tag manager.
/// </summary>
internal static class TagUi
{
    private const int MaxTagLength = 40;

    private static Brush ThemeBrush(string key, ElementTheme theme) => ThemeBrushes.Get(key, theme);

    private static string ClipCountText(ILocalizationService loc, int count) => $"{count} {loc.FormatPlural("Plural_Clip", count)}";

    /// <summary>A "Tags" submenu for one clip: a toggle per tag, then "New tag…".</summary>
    public static MenuFlyoutSubItem BuildClipSubmenu(HomeViewModel home, GameClip clip, XamlRoot? root, ElementTheme theme, string titleKey)
    {
        var loc = App.GetService<ILocalizationService>();
        var sub = new MenuFlyoutSubItem { Text = loc[titleKey], Icon = new FontIcon { Glyph = "" } };

        foreach (var tag in home.TagLibrary.ToList())
        {
            string name = tag;
            var item = new ToggleMenuFlyoutItem
            {
                Text = name,
                IsChecked = clip.Tags.Contains(name, StringComparer.CurrentCultureIgnoreCase)
            };
            item.Click += (s, e) => home.SetClipTag(clip, name, item.IsChecked);
            sub.Items.Add(item);
        }
        if (sub.Items.Count > 0) sub.Items.Add(new MenuFlyoutSeparator());

        var add = new MenuFlyoutItem { Text = loc["Tags_New"], Icon = new FontIcon { Glyph = "" } };
        add.Click += async (s, e) =>
        {
            string? name = await PromptNewTagAsync(root, theme);
            if (name != null) home.SetClipTag(clip, home.CreateTag(name), true);
        };
        sub.Items.Add(add);
        return sub;
    }

    /// <summary>Asks for a tag name; null when cancelled.</summary>
    public static async Task<string?> PromptNewTagAsync(XamlRoot? root, ElementTheme theme)
    {
        if (root == null) return null;
        var loc = App.GetService<ILocalizationService>();
        var box = new TextBox { PlaceholderText = loc["Tags_NamePlaceholder"], MaxLength = MaxTagLength, MinWidth = 300, RequestedTheme = theme };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = loc["Tags_NewTitle"],
            Content = box,
            PrimaryButtonText = loc["Tags_Create"],
            CloseButtonText = loc["Tags_Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        box.TextChanged += (s, e) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;
        box.Loaded += (s, e) => box.Focus(FocusState.Programmatic);

        var result = await dialog.ShowAsync();
        string name = box.Text.Trim();
        return result == ContentDialogResult.Primary && name.Length > 0 ? name : null;
    }

    /// <summary>The tag manager: rename, delete (with how many clips are affected) and add tags.</summary>
    public static async Task ShowManageDialogAsync(HomeViewModel home, XamlRoot? root, ElementTheme theme)
    {
        if (root == null) return;
        var loc = App.GetService<ILocalizationService>();
        var primary = ThemeBrush("TextFillColorPrimaryBrush", theme);
        var secondary = ThemeBrush("TextFillColorSecondaryBrush", theme);
        var tertiary = ThemeBrush("TextFillColorTertiaryBrush", theme);
        var subtle = (Style)Application.Current.Resources["SubtleButtonStyle"];

        var list = new StackPanel { Spacing = 2 };
        var scroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 0)
        };

        var addBox = new TextBox { PlaceholderText = loc["Tags_AddPlaceholder"], MaxLength = MaxTagLength, RequestedTheme = theme };
        var addButton = new Button { Content = loc["Tags_Add"], IsEnabled = false, RequestedTheme = theme };
        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(addBox);
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(addButton);

        var panel = new StackPanel { Spacing = 14, Width = 400, RequestedTheme = theme };
        panel.Children.Add(scroll);
        panel.Children.Add(addRow);

        string? renaming = null;
        string? deleting = null;

        Button IconButton(string glyph, string tip, Action click)
        {
            var b = new Button
            {
                Style = subtle,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon { Glyph = glyph, FontSize = 13 }
            };
            ToolTipService.SetToolTip(b, tip);
            b.Click += (s, e) => click();
            return b;
        }

        void Rebuild()
        {
            list.Children.Clear();
            var tags = home.TagLibrary.ToList();
            if (tags.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = loc["Tags_ManageEmpty"],
                    Foreground = secondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }
            foreach (var tag in tags) list.Children.Add(BuildRow(tag));
        }

        Grid BuildRow(string tag)
        {
            int count = home.CountWithTag(tag);
            var row = new Grid { ColumnSpacing = 6, MinHeight = 36 };
            for (int i = 0; i < 4; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            }

            void Place(FrameworkElement el, int column, int span = 1)
            {
                Grid.SetColumn(el, column);
                Grid.SetColumnSpan(el, span);
                row.Children.Add(el);
            }

            if (deleting == tag)
            {
                Place(new TextBlock
                {
                    Text = string.Format(loc["Tags_DeleteConfirm"], tag, ClipCountText(loc, count)),
                    Foreground = primary,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                }, 0, 2);
                var confirm = new Button { Content = loc["Tags_DeleteConfirmButton"], Style = (Style)Application.Current.Resources["AccentButtonStyle"], RequestedTheme = theme };
                confirm.Click += (s, e) =>
                {
                    deleting = null;
                    home.DeleteTag(tag);
                    Rebuild();
                };
                var cancel = new Button { Content = loc["Tags_Cancel"], RequestedTheme = theme };
                cancel.Click += (s, e) =>
                {
                    deleting = null;
                    Rebuild();
                };
                Place(confirm, 2);
                Place(cancel, 3);
                return row;
            }

            if (renaming == tag)
            {
                var box = new TextBox { Text = tag, MaxLength = MaxTagLength, VerticalAlignment = VerticalAlignment.Center, RequestedTheme = theme };
                void Commit()
                {
                    string name = box.Text.Trim();
                    renaming = null;
                    bool clash = home.TagLibrary.Any(t => !string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase) &&
                                                          string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));
                    if (name.Length > 0 && !clash) home.RenameTag(tag, name);
                    Rebuild();
                }
                void Cancel()
                {
                    renaming = null;
                    Rebuild();
                }
                box.KeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Enter)
                    {
                        e.Handled = true;
                        Commit();
                    }
                    else if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        e.Handled = true;
                        Cancel();
                    }
                };
                box.Loaded += (s, e) =>
                {
                    box.Focus(FocusState.Programmatic);
                    box.SelectAll();
                };
                Place(box, 0, 2);
                Place(IconButton("", loc["Tags_Rename"], Commit), 2);
                Place(IconButton("", loc["Tags_Cancel"], Cancel), 3);
                return row;
            }

            Place(new TextBlock
            {
                Text = tag,
                Foreground = primary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }, 0);
            Place(new TextBlock
            {
                Text = ClipCountText(loc, count),
                FontSize = 12,
                Foreground = tertiary,
                VerticalAlignment = VerticalAlignment.Center
            }, 1);
            Place(IconButton("", loc["Tags_Rename"], () =>
            {
                renaming = tag;
                deleting = null;
                Rebuild();
            }), 2);
            Place(IconButton("", loc["Tags_Delete"], () =>
            {
                deleting = tag;
                renaming = null;
                Rebuild();
            }), 3);
            return row;
        }

        void AddTag()
        {
            string name = addBox.Text.Trim();
            if (name.Length == 0) return;
            home.CreateTag(name);
            addBox.Text = string.Empty;
            Rebuild();
            addBox.Focus(FocusState.Programmatic);
        }

        addBox.TextChanged += (s, e) => addButton.IsEnabled = addBox.Text.Trim().Length > 0;
        addBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            AddTag();
        };
        addButton.Click += (s, e) => AddTag();

        Rebuild();
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = loc["Tags_ManageTitle"],
            Content = panel,
            CloseButtonText = loc["Tags_Done"],
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
