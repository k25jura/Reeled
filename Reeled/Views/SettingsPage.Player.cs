using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>Player customization in Settings: the bottom-bar editor and the OSD look.</summary>
public sealed partial class SettingsPage
{
    private Controls.ToolbarEditor? _toolbarEditor;
    private string? _toolbarEditorLanguage;
    private bool _syncingPlayerSettings;

    private static readonly int[] OsdDurations = { 700, 1100, 1800, 3000 };

    private void LocalizePlayerSettings(Services.ILocalizationService loc)
    {
        if (ToolbarEditorTitleText == null) return;
        ToolbarEditorTitleText.Text = loc["Toolbar_EditorTitle"];
        ToolbarEditorSubtitleText.Text = loc["Toolbar_SettingsSubtitle"];
        ToolbarResetButton.Content = loc["Toolbar_Reset"];
        OsdStyleTitleText.Text = loc["Osd_StyleTitle"];
        OsdStyleSubtitleText.Text = loc["Osd_StyleSubtitle"];
        OsdPositionLabel.Text = loc["Osd_Position"];
        OsdSizeLabel.Text = loc["Osd_Size"];
        OsdDurationLabel.Text = loc["Osd_Duration"];
        OsdBackdropLabel.Text = loc["Osd_Backdrop"];
        OsdTintLabel.Text = loc["Osd_Tint"];
        OsdDetailsLabel.Text = loc["Osd_Details"];

        // The editor's labels are localized when it's built, so it's rebuilt when the language changes. Building
        // it (four lists from XAML strings) is the costliest part of opening Settings, so not on every visit.
        if (_toolbarEditor == null || _toolbarEditorLanguage != loc.EffectiveLanguage)
        {
            _toolbarEditorLanguage = loc.EffectiveLanguage;
            _toolbarEditor = new Controls.ToolbarEditor { AllIds = PlayerPage.ToolbarItemIds };
            _toolbarEditor.LayoutChanged += (s, layout) =>
            {
                Storage.CurrentSettings.PlayerToolbar = layout.Clone();
                _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
            };
            ToolbarEditorHost.Content = _toolbarEditor;
        }
        // The bar may have been edited in the player since
        _toolbarEditor.SetLayout(Storage.CurrentSettings.PlayerToolbar ?? PlayerToolbarLayout.Default);

        _syncingPlayerSettings = true;
        OsdPositionCombo.ItemsSource = new[] { loc["Osd_PositionTop"], loc["Osd_PositionCenter"], loc["Osd_PositionBottom"] };
        OsdDurationCombo.ItemsSource = new[] { loc["Osd_DurationShort"], loc["Osd_DurationNormal"], loc["Osd_DurationLong"], loc["Osd_DurationLonger"] };
        var osd = Storage.CurrentSettings.Osd ?? new OsdPreferences();
        OsdPositionCombo.SelectedIndex = (int)osd.Position;
        int durationIndex = Array.FindIndex(OsdDurations, d => d >= osd.DurationMs);
        OsdDurationCombo.SelectedIndex = durationIndex < 0 ? OsdDurations.Length - 1 : durationIndex;
        OsdSizeSlider.Value = osd.FontSize;
        OsdBackdropToggle.IsOn = osd.ShowBackdrop;
        OsdTintToggle.IsOn = osd.TintByAction;
        OsdTintToggle.IsEnabled = osd.ShowBackdrop;
        OsdDetailsToggle.IsOn = osd.ShowDetails;

        PauseMediaTitleText.Text = loc["Settings_PauseMediaTitle"];
        PauseMediaSubtitleText.Text = loc["Settings_PauseMediaSubtitle"];
        ResumeMediaLabel.Text = loc["Settings_ResumeMediaLabel"];
        ResumeMediaHint.Text = loc["Settings_ResumeMediaHint"];
        var settings = Storage.CurrentSettings;
        PauseMediaToggle.IsOn = settings.PauseOtherMedia;
        ResumeMediaToggle.IsOn = settings.ResumeOtherMedia;
        ResumeMediaRow.Opacity = settings.PauseOtherMedia ? 1 : 0.5;
        ResumeMediaToggle.IsEnabled = settings.PauseOtherMedia;
        _syncingPlayerSettings = false;
    }

    private void OnBackgroundMediaToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingPlayerSettings || ResumeMediaToggle == null) return;
        var settings = Storage.CurrentSettings;
        settings.PauseOtherMedia = PauseMediaToggle.IsOn;
        settings.ResumeOtherMedia = ResumeMediaToggle.IsOn;
        ResumeMediaRow.Opacity = settings.PauseOtherMedia ? 1 : 0.5;
        ResumeMediaToggle.IsEnabled = settings.PauseOtherMedia;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnToolbarResetClick(object sender, RoutedEventArgs e)
    {
        var layout = PlayerToolbarLayout.Default;
        Storage.CurrentSettings.PlayerToolbar = layout;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
        _toolbarEditor?.SetLayout(layout);
    }

    private OsdPreferences Osd
    {
        get
        {
            var settings = Storage.CurrentSettings;
            return settings.Osd ??= new OsdPreferences();
        }
    }

    private void OnOsdSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPlayerSettings) return;
        if (OsdPositionCombo.SelectedIndex >= 0) Osd.Position = (OsdPosition)OsdPositionCombo.SelectedIndex;
        if (OsdDurationCombo.SelectedIndex >= 0) Osd.DurationMs = OsdDurations[OsdDurationCombo.SelectedIndex];
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnOsdSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingPlayerSettings || OsdSizeSlider == null) return;
        Osd.FontSize = OsdSizeSlider.Value;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }

    private void OnOsdToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPlayerSettings || OsdDetailsToggle == null) return;
        Osd.ShowBackdrop = OsdBackdropToggle.IsOn;
        Osd.TintByAction = OsdTintToggle.IsOn;
        Osd.ShowDetails = OsdDetailsToggle.IsOn;
        OsdTintToggle.IsEnabled = Osd.ShowBackdrop;
        _ = Storage.SaveSettingsAsync(Storage.CurrentSettings);
    }
}
