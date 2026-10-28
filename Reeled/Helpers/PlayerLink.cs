using System;
using System.IO;
using System.Text.RegularExpressions;
using Reeled.Models;

namespace Reeled.Helpers;

/// <summary>
/// Links to the clip player on reeled.app for Catbox and Litterbox shares. Everything about the clip goes
/// after "#" (never sent to a server): the host's file name, the expiry and the clip's name, e.g.
/// <c>…/clips/#l=abc123.mp4&amp;e=1793289600&amp;n=Ace%20on%20Haven%20B</c>. The raw link stays the default
/// copy because it plays right inside Discord; this one is for browsers.
/// </summary>
public static partial class PlayerLink
{
    public const string ClipsUrl = "https://k25jura.github.io/reeled.app/clips/";

    private const int NameMax = 80;

    [GeneratedRegex(@"^/[A-Za-z0-9_-]{1,64}\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex FilePath();

    /// <summary>The player link for a share, or null when its host isn't Catbox or Litterbox.</summary>
    public static string? For(SharedLink share)
    {
        if (!Uri.TryCreate(share.Link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        string? host = uri.Host switch
        {
            "litter.catbox.moe" => "l",
            "files.catbox.moe" => "c",
            _ => null
        };
        if (host == null || !FilePath().IsMatch(uri.AbsolutePath)) return null;

        var link = $"{ClipsUrl}#{host}={uri.AbsolutePath[1..]}";
        if (host == "l" && share.ExpiresUtc is DateTime expires)
        {
            link += "&e=" + new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }
        string name = Path.GetFileNameWithoutExtension(share.ClipPath).Trim();
        // Cut by characters, never through the middle of an emoji's surrogate pair
        if (name.Length > NameMax) name = name[..(char.IsHighSurrogate(name[NameMax - 1]) ? NameMax - 1 : NameMax)];
        if (name.Length > 0) link += "&n=" + Uri.EscapeDataString(name);
        return link;
    }
}
