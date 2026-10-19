using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.Views;

/// <summary>
/// Game icons on folders: the "Game icon" folder menu (Steam search, own image or program, remove) and
/// the chips in the horizontal navigation bar. The sidebar tree binds to the folder's own icon.
/// </summary>
public sealed partial class HomePage
{
    private void WireGameIcons()
    {
        // Chips are built in code, so they're rebuilt when icons arrive or change
        ViewModel.GameIconsChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (IsHorizontalNav) BuildHorizontalNav();
        });
    }

    /// <summary>A rounded game icon for chips and menus.</summary>
    private static FrameworkElement CreateGameIconElement(ImageSource source, double size) => new Grid
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
        Children = { new Image { Source = source, Stretch = Stretch.UniformToFill } }
    };

    /// <summary>
    /// Adds the "Game icon" submenu to a folder's menu. A watch folder that holds game folders is a
    /// library rather than a game, so it gets none.
    /// </summary>
    private void AddGameIconMenu(IList<MenuFlyoutItemBase> items, DirectoryNode node)
    {
        if (node.IsWatchRoot && node.SubDirectories.Count > 0) return;
        var loc = LayoutLoc;
        items.Add(new MenuFlyoutSeparator());

        var sub = new MenuFlyoutSubItem { Text = loc["GameIcon_Menu"], Icon = new FontIcon { Glyph = "" } };
        var find = new MenuFlyoutItem { Text = loc["GameIcon_Find"], Icon = new FontIcon { Glyph = "" } };
        find.Click += async (s, e) => await ShowSteamIconDialogAsync(node);
        var choose = new MenuFlyoutItem { Text = loc["GameIcon_Choose"], Icon = new FontIcon { Glyph = "" } };
        choose.Click += async (s, e) => await ChooseCustomIconAsync(node);
        var remove = new MenuFlyoutItem { Text = loc["GameIcon_Remove"], Icon = new FontIcon { Glyph = "" }, IsEnabled = node.HasGameIcon };
        remove.Click += (s, e) => ViewModel.RemoveIcon(node);
        sub.Items.Add(find);
        sub.Items.Add(choose);
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(remove);
        items.Add(sub);
    }

    private async Task ChooseCustomIconAsync(DirectoryNode node)
    {
        var loc = LayoutLoc;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail
            };
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".ico", ".exe", ".lnk" }) picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            string? path = await GameIconService.CacheCustomImageAsync(file, GameIconService.KeyFor(node.DisplayName));
            if (path == null)
            {
                await ShowErrorDialogAsync(loc["GameIcon_PickFailed"]);
                return;
            }
            ViewModel.SetCustomIcon(node, path);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("GameIcon custom", ex.Message);
            await ShowErrorDialogAsync(loc["GameIcon_PickFailed"]);
        }
    }

    /// <summary>Search the Steam store and pick the game whose icon this folder should use.</summary>
    private async Task ShowSteamIconDialogAsync(DirectoryNode node)
    {
        var loc = LayoutLoc;
        var theme = DialogTheme;
        var secondary = GetThemeResourceBrush("TextFillColorSecondaryBrush");

        var searchBox = new TextBox
        {
            Text = node.DisplayName,
            PlaceholderText = loc["GameIcon_SearchPlaceholder"],
            RequestedTheme = theme
        };
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            RequestedTheme = theme
        };
        var ring = new ProgressRing { Width = 24, Height = 24, IsActive = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock
        {
            Foreground = secondary,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 0),
            Visibility = Visibility.Collapsed
        };
        var body = new Grid { Height = 280 };
        body.Children.Add(results);
        body.Children.Add(ring);
        body.Children.Add(status);

        var panel = new StackPanel { Spacing = 12, Width = 420, RequestedTheme = theme };
        panel.Children.Add(searchBox);
        panel.Children.Add(body);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = theme,
            Title = loc["GameIcon_SearchTitle"],
            Content = panel,
            PrimaryButtonText = loc["GameIcon_Use"],
            CloseButtonText = loc["GameIcon_Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        CancellationTokenSource? cts = null;

        void ShowStatus(string? text)
        {
            status.Text = text ?? string.Empty;
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // Typing is debounced so the store isn't asked on every keystroke
        async void Search(bool immediate)
        {
            cts?.Cancel();
            var mine = cts = new CancellationTokenSource();
            string term = searchBox.Text.Trim();
            results.Items.Clear();
            dialog.IsPrimaryButtonEnabled = false;
            ShowStatus(null);
            if (term.Length < 2)
            {
                ring.IsActive = false;
                return;
            }

            ring.IsActive = true;
            try
            {
                if (!immediate) await Task.Delay(400, mine.Token);
                var found = await Task.Run(() => GameIconService.SearchAsync(term, mine.Token), mine.Token);
                if (mine.IsCancellationRequested) return;
                foreach (var game in found) results.Items.Add(CreateSteamResultItem(game));
                ring.IsActive = false;
                if (found.Count == 0) ShowStatus(loc["GameIcon_NoResults"]);
            }
            catch (OperationCanceledException)
            {
                // A newer search took over
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("GameIcon search", ex.Message);
                if (mine.IsCancellationRequested) return;
                ring.IsActive = false;
                ShowStatus(loc["GameIcon_SearchError"]);
            }
        }

        searchBox.TextChanged += (s, e) => Search(immediate: false);
        searchBox.Loaded += (s, e) =>
        {
            searchBox.Focus(FocusState.Programmatic);
            searchBox.SelectAll();
            Search(immediate: true);
        };
        results.SelectionChanged += (s, e) => dialog.IsPrimaryButtonEnabled = results.SelectedItem != null;
        dialog.Closed += (s, e) => cts?.Cancel();

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            if ((results.SelectedItem as ListViewItem)?.Tag is not SteamGame game)
            {
                args.Cancel = true;
                return;
            }
            // Stay open until the icon is downloaded, so a failure can be answered with another pick
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                ShowStatus(null);
                ring.IsActive = true;
                await ViewModel.SetSteamIconAsync(node, game);
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("GameIcon apply", ex.Message);
                args.Cancel = true;
                dialog.IsPrimaryButtonEnabled = true;
                ShowStatus(loc["GameIcon_ApplyFailed"]);
            }
            finally
            {
                ring.IsActive = false;
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private ListViewItem CreateSteamResultItem(SteamGame game)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var art = new Grid
        {
            Width = 88,
            Height = 33,
            CornerRadius = new CornerRadius(4),
            Background = GetThemeResourceBrush("LayerFillColorAltBrush")
        };
        if (!string.IsNullOrEmpty(game.CapsuleUrl) && Uri.TryCreate(game.CapsuleUrl, UriKind.Absolute, out var uri))
        {
            art.Children.Add(new Image { Source = new BitmapImage { UriSource = uri, DecodePixelWidth = 176 }, Stretch = Stretch.UniformToFill });
        }
        row.Children.Add(art);

        var name = new TextBlock { Text = game.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        return new ListViewItem { Content = row, Tag = game };
    }
}
