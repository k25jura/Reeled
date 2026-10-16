using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Reeled.Helpers;

/// <summary>
/// Renders GitHub release notes (Markdown) into a RichTextBlock: headings, bold sub-headings, bullet and
/// numbered lists, **bold**, *italic*, `code` and links. Sections that only matter on the download page
/// (installation, requirements, checksums, the compare link) are left out.
/// </summary>
public static partial class ReleaseNotes
{
    [GeneratedRegex(@"(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)\s]+\)|(?<![\w*])\*[^*\s][^*]*\*(?![\w*]))")]
    private static partial Regex InlinePattern();

    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
    private static partial Regex NumberedPattern();

    [GeneratedRegex(@"^(installation|installing|requirements|system requirements|verification|checksums?|sha-?256|встановлення|вимоги|перевірка)", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedSectionPattern();

    public static void Render(RichTextBlock target, string? markdown)
    {
        target.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(markdown)) return;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int skipLevel = 0;      // heading level of a skipped section, 0 = not skipping
        bool inCodeBlock = false;
        bool pendingGap = false;

        foreach (var raw in lines)
        {
            string line = raw.TrimEnd();
            string trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }
            if (inCodeBlock) continue;

            int headingLevel = 0;
            while (headingLevel < trimmed.Length && trimmed[headingLevel] == '#') headingLevel++;
            if (headingLevel > 0 && headingLevel < trimmed.Length && trimmed[headingLevel] == ' ')
            {
                string headingText = trimmed[(headingLevel + 1)..].Trim();
                if (skipLevel > 0 && headingLevel > skipLevel) continue;
                skipLevel = 0;

                // The top heading just repeats the version shown above the notes
                if (headingLevel == 1) continue;
                if (SkippedSectionPattern().IsMatch(headingText))
                {
                    skipLevel = headingLevel;
                    continue;
                }

                var heading = new Paragraph
                {
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, target.Blocks.Count == 0 ? 0 : 12, 0, 4)
                };
                AppendInline(heading.Inlines, headingText);
                target.Blocks.Add(heading);
                pendingGap = false;
                continue;
            }

            if (skipLevel > 0) continue;

            if (trimmed.Length == 0)
            {
                pendingGap = target.Blocks.Count > 0;
                continue;
            }
            if (trimmed is "---" or "***" or "___") continue;
            if (trimmed.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase)) continue;

            double top = pendingGap ? 6 : 0;
            pendingGap = false;

            // A line that is only bold text works as a small group heading ("**Library**")
            if (trimmed.StartsWith("**", StringComparison.Ordinal) && trimmed.EndsWith("**", StringComparison.Ordinal) &&
                trimmed.Length > 4 && trimmed.IndexOf("**", 2, StringComparison.Ordinal) == trimmed.Length - 2)
            {
                var group = new Paragraph
                {
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, Math.Max(top, 6), 0, 2)
                };
                group.Inlines.Add(new Run { Text = trimmed[2..^2] });
                target.Blocks.Add(group);
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                int indent = line.Length - line.TrimStart().Length;
                target.Blocks.Add(CreateListItem("•", trimmed[2..], top, indent >= 2));
                continue;
            }

            var numbered = NumberedPattern().Match(trimmed);
            if (numbered.Success)
            {
                target.Blocks.Add(CreateListItem(numbered.Groups[1].Value + ".", numbered.Groups[2].Value, top, false));
                continue;
            }

            var paragraph = new Paragraph { Margin = new Thickness(0, top, 0, 2) };
            AppendInline(paragraph.Inlines, trimmed);
            target.Blocks.Add(paragraph);
        }
    }

    private static Paragraph CreateListItem(string marker, string text, double top, bool nested)
    {
        // Hanging indent so wrapped lines align with the text, not the bullet
        double left = nested ? 30 : 14;
        var item = new Paragraph
        {
            Margin = new Thickness(left, top + 1, 0, 1),
            TextIndent = -12
        };
        item.Inlines.Add(new Run
        {
            Text = marker + "  ",
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });
        AppendInline(item.Inlines, text);
        return item;
    }

    private static void AppendInline(InlineCollection inlines, string text)
    {
        int index = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > index)
            {
                inlines.Add(new Run { Text = text[index..match.Index] });
            }

            string token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal))
            {
                inlines.Add(new Run { Text = token[2..^2], FontWeight = FontWeights.SemiBold });
            }
            else if (token.StartsWith('`'))
            {
                inlines.Add(new Run
                {
                    Text = token[1..^1],
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                });
            }
            else if (token.StartsWith('['))
            {
                int split = token.IndexOf("](", StringComparison.Ordinal);
                string label = token[1..split];
                string url = token[(split + 2)..^1];
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    var link = new Hyperlink { NavigateUri = uri, UnderlineStyle = UnderlineStyle.None };
                    link.Inlines.Add(new Run { Text = label });
                    inlines.Add(link);
                }
                else
                {
                    inlines.Add(new Run { Text = label });
                }
            }
            else
            {
                inlines.Add(new Run { Text = token[1..^1], FontStyle = Windows.UI.Text.FontStyle.Italic });
            }
            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            inlines.Add(new Run { Text = text[index..] });
        }
    }
}
