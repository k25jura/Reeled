using System;
using System.Globalization;

namespace Reeled.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    string EffectiveLanguage { get; }
    CultureInfo CurrentCulture { get; }

    string this[string key] { get; }
    string GetString(string key);
    string FormatPlural(string keyPrefix, int count);

    void SetLanguage(string languageCode);
    event EventHandler? LanguageChanged;
}
