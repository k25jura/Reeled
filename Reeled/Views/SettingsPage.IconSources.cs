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
        Helpers.Motion.Reveal(IconSourcesBody, open);

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
    }
}
