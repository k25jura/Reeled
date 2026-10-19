using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Reeled.Helpers;

/// <summary>One timed subtitle line.</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Reads text subtitle files (SRT, WebVTT, ASS/SSA) so Reeled can draw them itself with the user's
/// style, live. Formatting tags are stripped; line breaks are kept.
/// </summary>
public static partial class SubtitleParser
{
    public static bool CanParse(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".srt" or ".vtt" or ".ass" or ".ssa";

    public static List<SubtitleCue> Parse(string path)
    {
        string text = ReadText(path);
        var cues = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ass" or ".ssa" => ParseAss(text),
            _ => ParseSrtOrVtt(text)
        };
        cues.Sort((a, b) => a.Start.CompareTo(b.Start));
        return cues;
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // UTF-8 (with or without BOM) first; fall back to the system ANSI page for old files
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    [GeneratedRegex(@"(?<s>\d{1,2}:\d{2}(?::\d{2})?[.,]\d{1,3})\s*-->\s*(?<e>\d{1,2}:\d{2}(?::\d{2})?[.,]\d{1,3})")]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"<[^>]+>|\{\\[^}]*\}")]
    private static partial Regex TagRegex();

    private static List<SubtitleCue> ParseSrtOrVtt(string text)
    {
        var cues = new List<SubtitleCue>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = TimingRegex().Match(lines[i]);
            if (!m.Success) continue;
            var sb = new StringBuilder();
            int j = i + 1;
            for (; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[j].Trim());
            }
            string body = Clean(sb.ToString());
            if (body.Length > 0 && TryTime(m.Groups["s"].Value, out var s) && TryTime(m.Groups["e"].Value, out var e))
            {
                cues.Add(new SubtitleCue(s, e, body));
            }
            i = j;
        }
        return cues;
    }

    private static List<SubtitleCue> ParseAss(string text)
    {
        var cues = new List<SubtitleCue>();
        int startIdx = 1, endIdx = 2, textIdx = 9;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase) && line.Contains("Text", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line[7..].Split(',').Select(f => f.Trim()).ToList();
                startIdx = Math.Max(0, fields.FindIndex(f => f.Equals("Start", StringComparison.OrdinalIgnoreCase)));
                endIdx = Math.Max(0, fields.FindIndex(f => f.Equals("End", StringComparison.OrdinalIgnoreCase)));
                textIdx = Math.Max(0, fields.FindIndex(f => f.Equals("Text", StringComparison.OrdinalIgnoreCase)));
                continue;
            }
            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line[9..].Split(',', textIdx + 1);
            if (parts.Length <= textIdx) continue;
            string body = Clean(parts[textIdx].Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " "));
            if (body.Length > 0 && TryTime(parts[startIdx].Trim(), out var s) && TryTime(parts[endIdx].Trim(), out var e))
            {
                cues.Add(new SubtitleCue(s, e, body));
            }
        }
        return cues;
    }

    private static string Clean(string s) => System.Net.WebUtility.HtmlDecode(TagRegex().Replace(s, string.Empty)).Trim();

    private static bool TryTime(string value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        value = value.Replace(',', '.');
        var parts = value.Split(':');
        try
        {
            double seconds = parts.Length switch
            {
                3 => int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600 + int.Parse(parts[1], CultureInfo.InvariantCulture) * 60 + double.Parse(parts[2], CultureInfo.InvariantCulture),
                2 => int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + double.Parse(parts[1], CultureInfo.InvariantCulture),
                _ => double.Parse(value, CultureInfo.InvariantCulture)
            };
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The cue showing at a time (binary search; overlapping cues are joined).</summary>
    public static string? TextAt(IReadOnlyList<SubtitleCue> cues, TimeSpan t)
    {
        int lo = 0, hi = cues.Count - 1, first = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (cues[mid].Start <= t) { first = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (first < 0) return null;
        var active = new List<string>();
        // Walk back over earlier cues that may still be running
        for (int i = first; i >= 0 && i > first - 8; i--)
        {
            if (cues[i].Start <= t && cues[i].End > t) active.Insert(0, cues[i].Text);
        }
        return active.Count == 0 ? null : string.Join("\n", active);
    }
}
