using System;
using System.Security.Cryptography;
using System.Text;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>
/// The keys for the optional icon providers. They live in settings.json encrypted with DPAPI (current
/// Windows user), and in memory here, decrypted, so providers never touch the settings file.
/// </summary>
public static class GameIconCredentials
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Reeled.GameIcons.v1");

    public static string? SteamGridDbKey { get; private set; }
    public static string? IgdbClientId { get; private set; }
    public static string? IgdbClientSecret { get; private set; }

    public static bool IsConfigured(IconProvider provider) => provider switch
    {
        IconProvider.SteamGridDb => !string.IsNullOrWhiteSpace(SteamGridDbKey),
        IconProvider.Igdb => !string.IsNullOrWhiteSpace(IgdbClientId) && !string.IsNullOrWhiteSpace(IgdbClientSecret),
        _ => true
    };

    /// <summary>Reads the (decrypted) credentials from settings.</summary>
    public static void Load(AppSettings settings)
    {
        SteamGridDbKey = Unprotect(settings.ProtectedSteamGridDbKey);
        IgdbClientId = string.IsNullOrWhiteSpace(settings.IgdbClientId) ? null : settings.IgdbClientId.Trim();
        IgdbClientSecret = Unprotect(settings.ProtectedIgdbClientSecret);
    }

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain.Trim()), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Settings copied from another Windows account can't be decrypted; the key just needs entering again
            Helpers.Log.Error("GameIcon credentials", ex.Message);
            return null;
        }
    }
}
