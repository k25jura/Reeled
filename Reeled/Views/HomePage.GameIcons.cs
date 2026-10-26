using Reeled.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
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
/// Game icons on folders: the "Game icon" folder menu (search Steam / SteamGridDB / IGDB, own image or
/// program, remove) and the chips in the horizontal navigation bar. The sidebar tree binds to the
/// folder's own icon.
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

    /// <summary>The folder dropdown (top/bottom navigation) that is open, so a dialog can be shown on a clear screen.</summary>
    private Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? _folderDropdown;

    private void TrackFolderDropdown(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase menu)
    {
        _folderDropdown = menu;
        menu.Closed += (s, e) =>
        {
            if (_folderDropdown == menu) _folderDropdown = null;
        };
    }

    /// <summary>
    /// Closes the folder dropdown a menu command was chosen from (its context menu closes by itself) and waits
    /// until it is gone, so a dialog or file picker doesn't open underneath it.
    /// </summary>
    private async Task CloseFolderMenusAsync()
    {
        var menu = _folderDropdown;
        if (menu != null)
        {
            var closed = new TaskCompletionSource();
            void OnClosed(object? s, object e) => closed.TrySetResult();
            menu.Closed += OnClosed;
            menu.Hide();
            // Hide() animates; don't wait forever should Closed never come
            await Task.WhenAny(closed.Task, Task.Delay(500));
            menu.Closed -= OnClosed;
            _folderDropdown = null;
        }
        // Let the dispatcher finish the closing before the next popup opens
        await Task.Yield();
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
        find.Click += async (s, e) =>
        {
            await CloseFolderMenusAsync();
            await ShowIconDialogAsync(node);
        };
        var choose = new MenuFlyoutItem { Text = loc["GameIcon_Choose"], Icon = new FontIcon { Glyph = "" } };
        choose.Click += async (s, e) =>
        {
            await CloseFolderMenusAsync();
            await ChooseCustomIconAsync(node);
        };
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

    /// <summary>Search the icon providers and pick the game whose icon this folder should use.</summary>
    private async Task ShowIconDialogAsync(DirectoryNode node)
    {
        var loc = LayoutLoc;
        var theme = DialogTheme;
        var secondary = GetThemeResourceBrush("TextFillColorSecondaryBrush");
        GameIconCredentials.Load(App.GetService<ILocalStorageService>().CurrentSettings);

        var searchBox = new TextBox
        {
            Text = node.DisplayName,
            PlaceholderText = loc["GameIcon_SearchPlaceholder"],
            RequestedTheme = theme
        };

        // "All" searches every provider that is set up; its tag is null
        var filter = new SelectorBar { RequestedTheme = theme };
        filter.Items.Add(new SelectorBarItem { Text = loc["GameIcon_ProviderAll"], Tag = null });
        foreach (var provider in GameIconService.Providers)
        {
            var item = new SelectorBarItem { Text = provider.DisplayName, Tag = provider.Id };
            if (!provider.IsConfigured)
            {
                item.IsEnabled = false;
                ToolTipService.SetToolTip(item, loc["GameIcon_ProviderNotSet"]);
            }
            filter.Items.Add(item);
        }
        filter.SelectedItem = filter.Items[0];

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
        panel.Children.Add(filter);
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

        // Typing is debounced so the providers aren't asked on every keystroke
        async void Search(bool immediate)
        {
            cts?.Cancel();
            var mine = cts = new CancellationTokenSource();
            string term = searchBox.Text.Trim();
            var chosen = (filter.SelectedItem as SelectorBarItem)?.Tag as IconProvider?;
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
                var found = await Task.Run(() => chosen is IconProvider only
                    ? GameIconService.SearchAsync(only, term, mine.Token)
                    : GameIconService.SearchAllAsync(term, mine.Token), mine.Token);
                if (mine.IsCancellationRequested) return;
                var pending = new List<(Grid Art, GameIconResult Game)>();
                foreach (var game in found)
                {
                    results.Items.Add(CreateIconResultItem(game, out var art));
                    if (game.ThumbnailUrl == null) pending.Add((art, game));
                }
                ring.IsActive = false;
                if (found.Count == 0) ShowStatus(loc["GameIcon_NoResults"]);
                if (pending.Count > 0) _ = LoadMissingThumbnailsAsync(pending, mine.Token);
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
        filter.SelectionChanged += (s, e) =>
        {
            if (filter.IsLoaded) Search(immediate: true);
        };
        results.SelectionChanged += (s, e) => dialog.IsPrimaryButtonEnabled = results.SelectedItem != null;
        dialog.Closed += (s, e) => cts?.Cancel();

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            if ((results.SelectedItem as ListViewItem)?.Tag is not GameIconResult game)
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
                await ViewModel.SetIconAsync(node, game);
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

        await dialog.ShowSafeAsync();
    }

    /// <summary>Some providers (SteamGridDB) only tell the picture when asked per game; fetch a few at a time.</summary>
    private async Task LoadMissingThumbnailsAsync(List<(Grid Art, GameIconResult Game)> pending, CancellationToken ct)
    {
        using var limit = new SemaphoreSlim(3);
        var tasks = pending.Select(async p =>
        {
            try
            {
                await limit.WaitAsync(ct);
                try
                {
                    string? url = await Task.Run(() => GameIconService.GetProvider(p.Game.Provider).ResolveThumbnailAsync(p.Game, ct), ct);
                    if (ct.IsCancellationRequested || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            p.Art.Children.Add(new Image { Source = new BitmapImage { UriSource = uri, DecodePixelWidth = 80 }, Stretch = Stretch.UniformToFill });
                    });
                }
                finally
                {
                    limit.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // The dialog moved on to another search
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("GameIcon thumbnail", ex.Message);
            }
        });
        await Task.WhenAll(tasks);
    }

    private ListViewItem CreateIconResultItem(GameIconResult game, out Grid art)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Icons are square wherever they come from (covers and capsules are cropped to fit, as on the folder)
        art = new Grid
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(4),
            Background = GetThemeResourceBrush("LayerFillColorAltBrush")
        };
        if (!string.IsNullOrEmpty(game.ThumbnailUrl) && Uri.TryCreate(game.ThumbnailUrl, UriKind.Absolute, out var uri))
        {
            art.Children.Add(new Image { Source = new BitmapImage { UriSource = uri, DecodePixelWidth = 80 }, Stretch = Stretch.UniformToFill });
        }
        row.Children.Add(art);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = game.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = GameIconService.GetProvider(game.Provider).DisplayName,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = GetThemeResourceBrush("TextFillColorSecondaryBrush")
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        return new ListViewItem { Content = row, Tag = game };
    }
}
