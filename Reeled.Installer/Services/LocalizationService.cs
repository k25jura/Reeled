using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Reeled.Installer.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private string _currentLanguage = "en";
    private readonly Dictionary<string, Dictionary<string, string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);

    public event Action? LanguageChanged;
    public string CurrentLanguage => _currentLanguage;

    public record LanguageInfo(string Code, string NativeName, string EnglishName);

    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } = new List<LanguageInfo>
    {
        new("en", "English", "English"),
        new("uk", "Українська", "Ukrainian")
    };

    public LocalizationService()
    {
        LoadAllDictionaries();
        DetectSystemLanguage();
    }

    private void DetectSystemLanguage()
    {
        try
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            string lang = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (_dictionaries.ContainsKey(lang))
            {
                _currentLanguage = lang;
            }
            else
            {
                _currentLanguage = "en";
            }
        }
        catch
        {
            _currentLanguage = "en";
        }
    }

    private void LoadAllDictionaries()
    {
        foreach (var lang in SupportedLanguages)
        {
            var dict = LoadDictionaryForLanguage(lang.Code);
            if (dict != null)
            {
                _dictionaries[lang.Code] = dict;
            }
        }
    }

    private Dictionary<string, string>? LoadDictionaryForLanguage(string code)
    {
        try
        {
            // 1. Try loading from Localization folder next to executable
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization", $"{code}.json");
            if (File.Exists(localPath))
            {
                string json = File.ReadAllText(localPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }

            // 2. Try loading from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"Reeled.Installer.Localization.{code}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
        }
        catch { }

        return null;
    }

    public void SetLanguage(string langCode)
    {
        if (string.Equals(_currentLanguage, langCode, StringComparison.OrdinalIgnoreCase))
            return;

        if (_dictionaries.ContainsKey(langCode))
        {
            _currentLanguage = langCode;
            LanguageChanged?.Invoke();
        }
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (_dictionaries.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
        {
            return val;
        }

        // Fallback to English
        if (_dictionaries.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
        {
            return enVal;
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        string pattern = GetString(key);
        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }
}
