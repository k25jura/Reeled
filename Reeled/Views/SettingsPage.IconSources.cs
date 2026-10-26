using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Reeled.Services;

namespace Reeled.Views;

/// <summary>Appearance, Library: the keys for the optional game icon providers (SteamGridDB, IGDB).</summary>
public sealed partial class SettingsPage
{
    private bool _iconSourcesOpen;

    private void OnIconSourcesToggleClick(object sender, RoutedEventArgs e)
    {
        // Open when closed or on its way closed (Opacity is 0 only once fully hidden or while fading out)
        bool open = !_iconSourcesOpen;
        _iconSourcesOpen = open;
        Helpers.Motion.Reveal(IconSourcesBody, open, null, SettingsScrollViewer, OnRevealScrolling);

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(IconSourcesChevronRotation, "Angle", open ? 180 : 0, Helpers.Motion.Medium));
        Helpers.Motion.BeginExact(sb);
    }

    /// <summary>Shows the stored keys (as dots for the secrets).</summary>
    private void LoadIconSources()
    {
        var (key, clientId, clientSecret) = ViewModel.GetIconSources();
        SgdbKeyBox.Password = key;
        IgdbIdBox.Text = clientId;
        IgdbSecretBox.Password = clientSecret;
        // Keys go back to dots every time the page is opened
        SetKeyRevealed(SgdbKeyReveal, SgdbKeyBox, false);
        SetKeyRevealed(IgdbSecretReveal, IgdbSecretBox, false);
    }

    /// <summary>
    /// PasswordBox's own peek button only appears for text typed since the box got focus, so it is
    /// missing for a stored key. This button is always there and toggles the key in place.
    /// </summary>
    private void OnRevealKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button button) return;
        var box = button == SgdbKeyReveal ? SgdbKeyBox : IgdbSecretBox;
        SetKeyRevealed(button, box, box.PasswordRevealMode != Microsoft.UI.Xaml.Controls.PasswordRevealMode.Visible);
    }

    private void SetKeyRevealed(Microsoft.UI.Xaml.Controls.Button button, Microsoft.UI.Xaml.Controls.PasswordBox box, bool revealed)
    {
        box.PasswordRevealMode = revealed
            ? Microsoft.UI.Xaml.Controls.PasswordRevealMode.Visible
            : Microsoft.UI.Xaml.Controls.PasswordRevealMode.Hidden;
        if (button.Content is Microsoft.UI.Xaml.Controls.FontIcon icon)
        {
            icon.Glyph = revealed ? "" : "";
        }
        var loc = App.GetService<ILocalizationService>();
        string label = loc[revealed ? "Appearance_IconHideKey" : "Appearance_IconShowKey"];
        AutomationProperties.SetName(button, label);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, label);
    }

    /// <summary>Saves the keys when they changed; the stored ones are encrypted.</summary>
    private void CommitIconSources()
    {
        if (SgdbKeyBox == null) return;
        ViewModel.SetIconSources(SgdbKeyBox.Password, IgdbIdBox.Text, IgdbSecretBox.Password);
    }

    private void OnIconSourcesLostFocus(object sender, RoutedEventArgs e) => CommitIconSources();

    private void LocalizeIconSources(ILocalizationService loc)
    {
        if (IconSourcesTitleText == null) return;
        IconSourcesTitleText.Text = loc["Appearance_IconSourcesTitle"];
        IconSourcesSubtitleText.Text = loc["Appearance_IconSourcesSubtitle"];
        AutomationProperties.SetName(IconSourcesToggle, loc["Appearance_IconSourcesToggle"]);
        SgdbKeyBox.Header = loc["Appearance_IconSgdbKey"];
        SgdbKeyBox.PlaceholderText = loc["Appearance_IconSgdbKeyPlaceholder"];
        SgdbKeyLink.Content = loc["Appearance_IconGetKey"];
        IgdbIdBox.Header = loc["Appearance_IconIgdbId"];
        IgdbSecretBox.Header = loc["Appearance_IconIgdbSecret"];
        IgdbHintText.Text = loc["Appearance_IconIgdbHint"];
        IgdbCredentialsLink.Content = loc["Appearance_IconGetCredentials"];
        SetKeyRevealed(SgdbKeyReveal, SgdbKeyBox, SgdbKeyBox.PasswordRevealMode == Microsoft.UI.Xaml.Controls.PasswordRevealMode.Visible);
        SetKeyRevealed(IgdbSecretReveal, IgdbSecretBox, IgdbSecretBox.PasswordRevealMode == Microsoft.UI.Xaml.Controls.PasswordRevealMode.Visible);
    }
}
