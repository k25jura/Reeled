using System;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Reeled.Helpers;

/// <summary>
/// In-app feedback form. It mirrors the repository's GitHub issue forms (bug report, feature request,
/// general feedback) and opens the matching form in the browser with the fields already filled in, so
/// nothing is sent without the user seeing it and no GitHub token lives in the app.
/// </summary>
public static class FeedbackDialog
{
    private const string IssuesUrl = "https://github.com/k25jura/Reeled/issues/new";
    // GitHub rejects very long URLs; leave room for encoding
    private const int MaxFieldLength = 2500;

    public static async System.Threading.Tasks.Task ShowAsync(XamlRoot root)
    {
        var loc = App.GetService<Services.ILocalizationService>();
        var settings = App.GetService<ViewModels.SettingsViewModel>();

        var kind = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { loc["Feedback_KindBug"], loc["Feedback_KindIdea"], loc["Feedback_KindOther"] },
            SelectedIndex = 0,
            Header = loc["Feedback_KindLabel"]
        };
        var title = new TextBox
        {
            Header = loc["Feedback_TitleLabel"],
            PlaceholderText = loc["Feedback_TitlePlaceholder"],
            MaxLength = 120
        };
        var details = new TextBox
        {
            Header = loc["Feedback_DetailsLabel"],
            PlaceholderText = loc["Feedback_DetailsPlaceholderBug"],
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 140,
            MaxLength = MaxFieldLength
        };
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(details, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto);
        var steps = new TextBox
        {
            Header = loc["Feedback_StepsLabel"],
            PlaceholderText = loc["Feedback_StepsPlaceholder"],
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            MaxLength = MaxFieldLength
        };
        var includeDiagnostics = new CheckBox
        {
            Content = loc["Feedback_IncludeDiagnostics"],
            IsChecked = true
        };
        var note = new TextBlock
        {
            Text = loc["Feedback_Note"],
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", App.CurrentTheme)
        };

        kind.SelectionChanged += (s, e) =>
        {
            bool bug = kind.SelectedIndex == 0;
            steps.Visibility = bug ? Visibility.Visible : Visibility.Collapsed;
            details.PlaceholderText = loc[kind.SelectedIndex switch
            {
                0 => "Feedback_DetailsPlaceholderBug",
                1 => "Feedback_DetailsPlaceholderIdea",
                _ => "Feedback_DetailsPlaceholderOther"
            }];
        };

        var panel = new StackPanel { Spacing = 14, Width = 460 };
        panel.Children.Add(kind);
        panel.Children.Add(title);
        panel.Children.Add(details);
        panel.Children.Add(steps);
        panel.Children.Add(includeDiagnostics);
        panel.Children.Add(note);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = loc["Feedback_Title"],
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = loc["Feedback_Send"],
            CloseButtonText = loc["Feedback_Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            RequestedTheme = App.CurrentTheme
        };
        void Validate() => dialog.IsPrimaryButtonEnabled = title.Text.Trim().Length > 0 && details.Text.Trim().Length > 0;
        title.TextChanged += (s, e) => Validate();
        details.TextChanged += (s, e) => Validate();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string diagnostics = includeDiagnostics.IsChecked == true ? settings.BuildDiagnosticsReport(includeLogPath: false) : string.Empty;
        string url = BuildIssueUrl(kind.SelectedIndex, title.Text.Trim(), details.Text.Trim(), steps.Text.Trim(), diagnostics, settings.CurrentVersionTag);
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private static string BuildIssueUrl(int kind, string title, string details, string steps, string diagnostics, string version)
    {
        var query = new StringBuilder();
        void Add(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (value.Length > MaxFieldLength) value = value[..MaxFieldLength];
            query.Append(query.Length == 0 ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        string context = string.IsNullOrEmpty(diagnostics) ? string.Empty : "```text\n" + diagnostics + "\n```";
        switch (kind)
        {
            case 0:
                Add("template", "bug_report.yml");
                Add("title", "[Bug] " + title);
                Add("version", version.TrimStart('v'));
                Add("description", details);
                Add("reproduction", steps);
                Add("diagnostics", context);
                break;
            case 1:
                Add("template", "feature_request.yml");
                Add("title", "[Idea] " + title);
                Add("solution", details);
                Add("context", context);
                break;
            default:
                Add("template", "feedback.yml");
                Add("title", "[Feedback] " + title);
                Add("feedback", details);
                Add("diagnostics", context);
                break;
        }
        return IssuesUrl + query;
    }
}
