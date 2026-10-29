using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Reeled.Helpers;

namespace Reeled.Controls;

/// <summary>
/// The floating card that shows a file operation: what is being copied or moved, a progress bar and a Cancel
/// button; afterwards it holds a one-line result for a few seconds.
/// </summary>
public sealed class FileOperationCard : UserControl
{
    private readonly FontIcon _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly ProgressBar _bar;
    private readonly Button _cancel;

    public event Action? CancelRequested;

    public FileOperationCard()
    {
        MinWidth = 340;
        MaxWidth = 520;
        HorizontalAlignment = HorizontalAlignment.Center;
        var acrylic = new Microsoft.UI.Xaml.Media.AcrylicBrush();
        var root = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 16, 12),
            Background = acrylic
        };
        ThemeBrushes.Track(root, () =>
        {
            Backdrop.ApplyAppSurface(acrylic, ThemeBrushes.ThemeOf(root));
            root.BorderBrush = ThemeBrushes.Get("SurfaceStrokeColorFlyoutBrush", root);
        });

        _icon = new FontIcon { FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _detail = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        ThemeBrushes.Bind(_detail, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _bar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
        _cancel = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        _cancel.Click += (s, e) => CancelRequested?.Invoke();

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_detail);
        text.Children.Add(_bar);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(_cancel, 2);
        grid.Children.Add(_cancel);
        root.Child = grid;
        Content = root;
    }

    /// <summary>An operation starts: indeterminate until the first progress report.</summary>
    public void ShowProgress(string title, string glyph, string cancelText)
    {
        _icon.Glyph = glyph;
        _icon.Foreground = ThemeBrushes.Get("AccentTextFillColorPrimaryBrush", this);
        _title.Text = title;
        _detail.Text = string.Empty;
        _detail.Visibility = Visibility.Collapsed;
        _bar.Visibility = Visibility.Visible;
        _bar.IsIndeterminate = true;
        _bar.ShowError = false;
        _cancel.Content = cancelText;
        _cancel.IsEnabled = true;
        _cancel.Visibility = Visibility.Visible;
    }

    public void SetProgress(string detail, double fraction)
    {
        _detail.Text = detail;
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        _bar.IsIndeterminate = false;
        _bar.Value = Math.Clamp(fraction, 0, 1);
    }

    public void SetCancelling(string text)
    {
        _cancel.IsEnabled = false;
        _detail.Text = text;
        _detail.Visibility = Visibility.Visible;
    }

    /// <summary>The operation is over: a message with the action's icon (a check by default), or an error mark.</summary>
    public void ShowResult(string message, bool error, string? glyph = null)
    {
        _icon.Glyph = error ? "" : glyph ?? "";
        _icon.Foreground = ThemeBrushes.Get(error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush", this);
        _title.Text = message;
        _detail.Visibility = Visibility.Collapsed;
        _bar.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Collapsed;
    }
}
