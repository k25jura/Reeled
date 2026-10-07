using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Reeled.Services;

public class LocalizationService : ILocalizationService
{
    private string _currentLanguage = "System";
    private string _effectiveLanguage = "en";
    private CultureInfo _currentCulture = CultureInfo.InvariantCulture;

    private readonly Dictionary<string, Dictionary<string, string>> _loadedDictionaries = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage => _currentLanguage;
    public string EffectiveLanguage => _effectiveLanguage;
    public CultureInfo CurrentCulture => _currentCulture;

    public event EventHandler? LanguageChanged;

    public string this[string key] => GetString(key);

    public LocalizationService()
    {
        LoadJsonDictionaries();
        ApplyLanguage("System");
    }

    public void SetLanguage(string languageCode)
    {
        if (string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        ApplyLanguage(languageCode);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var list = new List<string>(_loadedDictionaries.Keys);
        if (!list.Contains("en", StringComparer.OrdinalIgnoreCase)) list.Add("en");
        if (!list.Contains("uk", StringComparer.OrdinalIgnoreCase)) list.Add("uk");
        return list;
    }

    private void LoadJsonDictionaries()
    {
        try
        {
            var searchPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Localization"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Localization"),
                Path.Combine(Environment.CurrentDirectory, "Reeled", "Localization"),
                Path.Combine(Environment.CurrentDirectory, "Localization")
            };

            string? localizationDir = null;
            foreach (var p in searchPaths)
            {
                if (Directory.Exists(p))
                {
                    localizationDir = p;
                    break;
                }
            }

            if (localizationDir != null)
            {
                foreach (var file in Directory.EnumerateFiles(localizationDir, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var dict = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.DictionaryStringString);
                        if (dict != null)
                        {
                            string langCode = Path.GetFileNameWithoutExtension(file);
                            if (dict.TryGetValue("__language_code", out var code) && !string.IsNullOrWhiteSpace(code))
                            {
                                langCode = code;
                            }
                            _loadedDictionaries[langCode] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { File.AppendAllText("reeled_crash.log", $"[Localization.Load Error {file}] {ex}\n"); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    private void ApplyLanguage(string languageCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "System" : languageCode;

        if (string.Equals(_currentLanguage, "System", StringComparison.OrdinalIgnoreCase))
        {
            _effectiveLanguage = DetectSystemLanguage();
        }
        else if (string.Equals(_currentLanguage, "uk", StringComparison.OrdinalIgnoreCase))
        {
            _effectiveLanguage = "uk";
        }
        else
        {
            _effectiveLanguage = "en";
        }

        try
        {
            _currentCulture = _effectiveLanguage == "uk" 
                ? new CultureInfo("uk-UA") 
                : new CultureInfo("en-US");
        }
        catch
        {
            _currentCulture = CultureInfo.InvariantCulture;
        }

        try
        {
            CultureInfo.DefaultThreadCurrentCulture = _currentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _currentCulture;
            CultureInfo.CurrentCulture = _currentCulture;
            CultureInfo.CurrentUICulture = _currentCulture;
        }
        catch { }
    }

    public static string DetectSystemLanguage()
    {
        try
        {
            var userLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            if (userLanguages != null && userLanguages.Count > 0)
            {
                foreach (var lang in userLanguages)
                {
                    if (lang.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
                        return "uk";
                    if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        return "en";
                }
            }
        }
        catch { }

        try
        {
            var uiCulture = CultureInfo.CurrentUICulture;
            if (uiCulture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase))
                return "uk";
        }
        catch { }

        try
        {
            var installedCulture = CultureInfo.InstalledUICulture;
            if (installedCulture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase))
                return "uk";
        }
        catch { }

        return "en";
    }

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        // 1. Check loaded JSON dictionary for effective language
        if (_loadedDictionaries.TryGetValue(_effectiveLanguage, out var dict) && dict.TryGetValue(key, out var val))
        {
            if (!string.IsNullOrEmpty(val)) return val;
        }

        // 2. Fallback to loaded English JSON dictionary
        if (_loadedDictionaries.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
        {
            if (!string.IsNullOrEmpty(enVal)) return enVal;
        }

        // 3. In-memory safety net
        var fallbackDict = _effectiveLanguage == "uk" ? UkrainianFallback : EnglishFallback;
        if (fallbackDict.TryGetValue(key, out var fbVal))
            return fbVal;

        if (EnglishFallback.TryGetValue(key, out var enFbVal))
            return enFbVal;

        return key;
    }

    public string FormatPlural(string keyPrefix, int count)
    {
        if (_effectiveLanguage == "uk")
        {
            int mod10 = count % 10;
            int mod100 = count % 100;

            if (mod10 == 1 && mod100 != 11)
            {
                return GetString($"{keyPrefix}_One");
            }
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20))
            {
                return GetString($"{keyPrefix}_Few");
            }
            return GetString($"{keyPrefix}_Many");
        }
        else
        {
            return count == 1 ? GetString($"{keyPrefix}_One") : GetString($"{keyPrefix}_Many");
        }
    }

    private static readonly Dictionary<string, string> EnglishFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["App_Title"] = "Reeled",
        ["Nav_Home"] = "Home",
        ["Nav_Favorites"] = "Favorites",
        ["Nav_SavedMoments"] = "Saved Moments",
        ["Nav_Folders"] = "Folders",
        ["Nav_AddFolder"] = "Add",
        ["Nav_Settings"] = "Settings",
        ["Date_Today"] = "Today",
        ["Date_Yesterday"] = "Yesterday",
        ["Plural_Clip_One"] = "clip",
        ["Plural_Clip_Many"] = "clips",
        ["Plural_Moment_One"] = "moment",
        ["Plural_Moment_Many"] = "moments",
        ["Updates_StatusTitle"] = "Reeled is up to date",
        ["Updates_StatusAvailable"] = "Update available",
        ["Updates_StatusReady"] = "Ready to install",
        ["Common_Cancel"] = "Cancel"
    };

    private static readonly Dictionary<string, string> UkrainianFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["App_Title"] = "Reeled",
        ["Nav_Home"] = "Головна",
        ["Nav_Favorites"] = "Вибране",
        ["Nav_SavedMoments"] = "Збережені моменти",
        ["Nav_Folders"] = "Папки",
        ["Nav_AddFolder"] = "Додати",
        ["Nav_Settings"] = "Налаштування",
        ["Date_Today"] = "Сьогодні",
        ["Date_Yesterday"] = "Вчора",
        ["Plural_Clip_One"] = "кліп",
        ["Plural_Clip_Few"] = "кліпи",
        ["Plural_Clip_Many"] = "кліпів",
        ["Plural_Moment_One"] = "момент",
        ["Plural_Moment_Few"] = "моменти",
        ["Plural_Moment_Many"] = "моментів",
        ["Updates_StatusTitle"] = "Reeled оновлено до останньої версії",
        ["Updates_StatusAvailable"] = "Доступне оновлення",
        ["Updates_StatusReady"] = "Готово до встановлення",
        ["Common_Cancel"] = "Скасувати"
    };
}
