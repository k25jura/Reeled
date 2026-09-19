using System;
using System.Collections.Generic;
using System.Globalization;

namespace Reeled.Services;

public class LocalizationService : ILocalizationService
{
    private string _currentLanguage = "System";
    private string _effectiveLanguage = "en";
    private CultureInfo _currentCulture = CultureInfo.InvariantCulture;

    public string CurrentLanguage => _currentLanguage;
    public string EffectiveLanguage => _effectiveLanguage;
    public CultureInfo CurrentCulture => _currentCulture;

    public event EventHandler? LanguageChanged;

    public string this[string key] => GetString(key);

    public LocalizationService()
    {
        ApplyLanguage("System");
    }

    public void SetLanguage(string languageCode)
    {
        if (string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        ApplyLanguage(languageCode);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
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

        var dict = _effectiveLanguage == "uk" ? UkrainianDictionary : EnglishDictionary;
        if (dict.TryGetValue(key, out var val))
            return val;

        if (_effectiveLanguage != "en" && EnglishDictionary.TryGetValue(key, out var fallback))
            return fallback;

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

    private static readonly Dictionary<string, string> EnglishDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common & Navigation
        ["App_Title"] = "Reeled",
        ["Nav_Home"] = "Home",
        ["Nav_Favorites"] = "Favorites",
        ["Nav_SavedMoments"] = "Saved Moments",
        ["Nav_Folders"] = "Folders",
        ["Nav_AddFolder"] = "Add",
        ["Nav_Settings"] = "Settings",
        ["Search_Placeholder"] = "Search clips...",
        ["Sort_NewestDate"] = "Newest Date",
        ["Sort_OldestDate"] = "Oldest Date",
        ["Sort_TitleAZ"] = "Title (A-Z)",
        ["Sort_TitleZA"] = "Title (Z-A)",
        ["Sort_Duration"] = "Duration",
        ["Sort_FileSize"] = "File Size",

        // Empty States
        ["Empty_NoClipsTitle"] = "No clips in library",
        ["Empty_NoClipsSubtitle"] = "Add your captures folder or drop video files to start watching.",
        ["Empty_NoFavoritesTitle"] = "No favorites yet",
        ["Empty_NoFavoritesSubtitle"] = "Click the heart icon on any clip to pin it to your favorites.",
        ["Empty_NoMomentsTitle"] = "No saved moments yet",
        ["Empty_NoMomentsSubtitle"] = "Add bookmarks and timestamps during video playback to revisit key highlights.",
        ["Empty_FolderTitle"] = "No clips in this folder",
        ["Empty_FolderSubtitle"] = "Choose another folder or add MP4/MKV video files to this directory.",

        // Clip Strings & Date
        ["Date_Today"] = "Today",
        ["Date_Yesterday"] = "Yesterday",
        ["Plural_Clip_One"] = "clip",
        ["Plural_Clip_Few"] = "clips",
        ["Plural_Clip_Many"] = "clips",
        ["Plural_Moment_One"] = "moment",
        ["Plural_Moment_Few"] = "moments",
        ["Plural_Moment_Many"] = "moments",
        ["Plural_File_One"] = "file",
        ["Plural_File_Few"] = "files",
        ["Plural_File_Many"] = "files",

        // Settings Header & Navigation Categories
        ["Settings_Title"] = "Settings",
        ["Settings_Back"] = "Back to Library",
        ["Cat_Folders"] = "Watch Folders",
        ["Cat_Playback"] = "Playback",
        ["Cat_Appearance"] = "Appearance",
        ["Cat_Language"] = "Language & Region",
        ["Cat_Storage"] = "Storage & Cache",
        ["Cat_Updates"] = "Updates",
        ["Cat_About"] = "About Reeled",

        // Watch Folders Section
        ["Folders_SectionTitle"] = "Watch Folders",
        ["Folders_SectionSubtitle"] = "Manage the local directories Reeled monitors for clips",
        ["Folders_ConfiguredTitle"] = "Configured Library Directories",
        ["Folders_ConfiguredSubtitle"] = "Reeled indexes videos from these folders and watches for new captures",
        ["Folders_AddButton"] = "Add Folder",
        ["Folders_EmptyTitle"] = "No folders added",
        ["Folders_EmptySubtitle"] = "Add folders containing your video clips to start browsing your library.",
        ["Folders_OpenTip"] = "Click to open folder in library view",
        ["Folders_RemoveTip"] = "Remove folder from library",

        // Playback Section
        ["Playback_SectionTitle"] = "Playback",
        ["Playback_SectionSubtitle"] = "Configure video player behavior and defaults",
        ["Playback_DefaultSpeedTitle"] = "Default playback speed",
        ["Playback_DefaultSpeedSubtitle"] = "Initial speed when opening new video clips",
        ["Playback_RememberSpeedTitle"] = "Remember playback speed",
        ["Playback_RememberSpeedSubtitle"] = "Preserve the last used playback speed across sessions",

        // Appearance Section
        ["Appearance_SectionTitle"] = "Appearance",
        ["Appearance_SectionSubtitle"] = "Customize the visual theme and animations of Reeled",
        ["Appearance_ThemeTitle"] = "App theme",
        ["Appearance_ThemeSubtitle"] = "Select which app theme to display",
        ["Appearance_ThemeWindows"] = "Use system setting",
        ["Appearance_ThemeDark"] = "Dark",
        ["Appearance_ThemeLight"] = "Light",
        ["Appearance_SkeletonTitle"] = "Skeleton loading animation",
        ["Appearance_SkeletonSubtitle"] = "Show animated placeholder cards while video clips load",

        // Language & Region Section
        ["Language_SectionTitle"] = "Language & Region",
        ["Language_SectionSubtitle"] = "Configure interface language, auto-detection, and regional date formatting",
        ["Language_AppLanguageTitle"] = "App language",
        ["Language_AppLanguageSubtitle"] = "Select your preferred display language for menus and buttons",
        ["Language_SystemDefault"] = "System default (Auto-detect)",
        ["Language_English"] = "English",
        ["Language_Ukrainian"] = "Українська (Ukrainian)",
        ["Language_DateFormatTitle"] = "Regional date & time format",
        ["Language_DateFormatSubtitle"] = "Dates, timestamps, and relative days are formatted to regional standards",
        ["Language_SamplePreview"] = "Sample preview",

        // Storage & Cache Section
        ["Storage_SectionTitle"] = "Storage & Cache",
        ["Storage_SectionSubtitle"] = "Manage disk space, cached files, and clip metadata",
        ["Storage_ThumbnailsTitle"] = "Thumbnail cache",
        ["Storage_ThumbnailsSubtitle"] = "Cached video preview thumbnails stored on disk for fast scrolling",
        ["Storage_ThumbnailsButton"] = "Clear Thumbnails",
        ["Storage_ClipCacheTitle"] = "Clip metadata cache",
        ["Storage_ClipCacheSubtitle"] = "Cached video durations, resolutions, and framerates for faster load times",
        ["Storage_ClipCacheButton"] = "Clear Cache",
        ["Storage_MomentsTitle"] = "Saved moments metadata",
        ["Storage_MomentsSubtitle"] = "Clear all saved bookmark moments and highlight markers from your library clips",
        ["Storage_MomentsButton"] = "Clear Moments",
        ["Storage_Status_MomentsCleared"] = "Saved moments metadata cleared ({0} moments removed)",
        ["Storage_Status_NoMoments"] = "No saved moments found",

        // Updates Section
        ["Updates_SectionTitle"] = "Updates",
        ["Updates_SectionSubtitle"] = "Keep Reeled up to date with the latest features, improvements, and fixes",
        ["Updates_StatusTitle"] = "Reeled is up to date",
        ["Updates_StatusChecking"] = "Checking for updates...",
        ["Updates_StatusAvailable"] = "Update available",
        ["Updates_StatusDownloading"] = "Downloading update...",
        ["Updates_StatusReady"] = "Ready to install",
        ["Updates_CheckButton"] = "Check for updates",
        ["Updates_CheckingButton"] = "Checking...",
        ["Updates_LastChecked"] = "Last checked: {0}",
        ["Updates_VersionBadge"] = "v1.0.0 (x64)",
        ["Updates_AutoCheckTitle"] = "Check for updates automatically",
        ["Updates_AutoCheckSubtitle"] = "Periodically check for new releases in the background",
        ["Updates_AvailableHeader"] = "Reeled Feature Update",
        ["Updates_AvailableNotes"] = "Includes Ukrainian localization, redesigned settings categories, and performance optimizations.",
        ["Updates_DownloadButton"] = "Download & Install",
        ["Updates_InstallButton"] = "Install and Restart",
        ["Updates_Size"] = "Size: 38.4 MB",

        // About Section
        ["About_Tagline"] = "Modern game clips viewer & player for Windows",
        ["About_Version"] = "Version 1.0.0",
        ["About_Framework"] = "Built with WinUI 3, Windows App SDK & LibVLC",
        ["About_Developer"] = "Crafted with passion for PC gamers and clip editors",
        ["About_GitHub"] = "GitHub Repository",
        ["About_Releases"] = "Release Notes",
        ["About_Issues"] = "Report an Issue",
        ["About_License"] = "MIT License • Free & Open Source"
    };

    private static readonly Dictionary<string, string> UkrainianDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common & Navigation
        ["App_Title"] = "Reeled",
        ["Nav_Home"] = "Головна",
        ["Nav_Favorites"] = "Вибране",
        ["Nav_SavedMoments"] = "Збережені моменти",
        ["Nav_Folders"] = "Папки",
        ["Nav_AddFolder"] = "Додати",
        ["Nav_Settings"] = "Налаштування",
        ["Search_Placeholder"] = "Пошук кліпів...",
        ["Sort_NewestDate"] = "Найновіші",
        ["Sort_OldestDate"] = "Найстаріші",
        ["Sort_TitleAZ"] = "Назва (А-Я)",
        ["Sort_TitleZA"] = "Назва (Я-А)",
        ["Sort_Duration"] = "Тривалість",
        ["Sort_FileSize"] = "Розмір файлу",

        // Empty States
        ["Empty_NoClipsTitle"] = "Немає кліпів у бібліотеці",
        ["Empty_NoClipsSubtitle"] = "Додайте папку із записами або перетягніть відеофайли, щоб почати перегляд.",
        ["Empty_NoFavoritesTitle"] = "Немає вибраних кліпів",
        ["Empty_NoFavoritesSubtitle"] = "Натисніть на значок серця на будь-якому кліпі, щоб закріпити його у вибраному.",
        ["Empty_NoMomentsTitle"] = "Немає збережених моментів",
        ["Empty_NoMomentsSubtitle"] = "Додавайте закладки та мітки часу під час перегляду відео, щоб повертатися до ключових моментів.",
        ["Empty_FolderTitle"] = "Немає кліпів у цій папці",
        ["Empty_FolderSubtitle"] = "Виберіть іншу папку або додайте відеофайли MP4/MKV до цієї директорії.",

        // Clip Strings & Date
        ["Date_Today"] = "Сьогодні",
        ["Date_Yesterday"] = "Вчора",
        ["Plural_Clip_One"] = "кліп",
        ["Plural_Clip_Few"] = "кліпи",
        ["Plural_Clip_Many"] = "кліпів",
        ["Plural_Moment_One"] = "момент",
        ["Plural_Moment_Few"] = "моменти",
        ["Plural_Moment_Many"] = "моментів",
        ["Plural_File_One"] = "файл",
        ["Plural_File_Few"] = "файли",
        ["Plural_File_Many"] = "файлів",

        // Settings Header & Navigation Categories
        ["Settings_Title"] = "Налаштування",
        ["Settings_Back"] = "Назад до бібліотеки",
        ["Cat_Folders"] = "Папки медіатеки",
        ["Cat_Playback"] = "Відтворення",
        ["Cat_Appearance"] = "Вигляд",
        ["Cat_Language"] = "Мова та регіон",
        ["Cat_Storage"] = "Пам'ять та кеш",
        ["Cat_Updates"] = "Оновлення",
        ["Cat_About"] = "Про Reeled",

        // Watch Folders Section
        ["Folders_SectionTitle"] = "Папки медіатеки",
        ["Folders_SectionSubtitle"] = "Керування локальними каталогами, які Reeled відстежує для відеокліпів",
        ["Folders_ConfiguredTitle"] = "Налаштовані папки бібліотеки",
        ["Folders_ConfiguredSubtitle"] = "Reeled індексує відео з цих папок і автоматично відстежує нові записи",
        ["Folders_AddButton"] = "Додати папку",
        ["Folders_EmptyTitle"] = "Не додано жодної папки",
        ["Folders_EmptySubtitle"] = "Додайте папки із записаними кліпами, щоб почати перегляд вашої медіатеки.",
        ["Folders_OpenTip"] = "Натисніть, щоб відкрити папку в бібліотеці",
        ["Folders_RemoveTip"] = "Видалити папку з бібліотеки",

        // Playback Section
        ["Playback_SectionTitle"] = "Відтворення",
        ["Playback_SectionSubtitle"] = "Налаштування параметрів відеопрогравача та поведінки за замовчуванням",
        ["Playback_DefaultSpeedTitle"] = "Швидкість відтворення за замовчуванням",
        ["Playback_DefaultSpeedSubtitle"] = "Початкова швидкість при відкритті нових відеокліпів",
        ["Playback_RememberSpeedTitle"] = "Запам'ятовувати швидкість відтворення",
        ["Playback_RememberSpeedSubtitle"] = "Зберігати останню використану швидкість між сеансами",

        // Appearance Section
        ["Appearance_SectionTitle"] = "Вигляд",
        ["Appearance_SectionSubtitle"] = "Налаштування кольорової теми та анімацій інтерфейсу Reeled",
        ["Appearance_ThemeTitle"] = "Тема застосунку",
        ["Appearance_ThemeSubtitle"] = "Виберіть тему інтерфейсу для відображення",
        ["Appearance_ThemeWindows"] = "Як у системі",
        ["Appearance_ThemeDark"] = "Темна",
        ["Appearance_ThemeLight"] = "Світла",
        ["Appearance_SkeletonTitle"] = "Анімація завантаження карток",
        ["Appearance_SkeletonSubtitle"] = "Показувати анімовані картки-заглушки під час завантаження кліпів",

        // Language & Region Section
        ["Language_SectionTitle"] = "Мова та регіон",
        ["Language_SectionSubtitle"] = "Налаштування мови інтерфейсу, автовизначення та форматування дати",
        ["Language_AppLanguageTitle"] = "Мова застосунку",
        ["Language_AppLanguageSubtitle"] = "Виберіть бажану мову відображення меню, кнопок та підказок",
        ["Language_SystemDefault"] = "Мова системи (Автовизначення)",
        ["Language_English"] = "English",
        ["Language_Ukrainian"] = "Українська",
        ["Language_DateFormatTitle"] = "Регіональний формат дати й часу",
        ["Language_DateFormatSubtitle"] = "Дати, мітки часу та дні відображаються згідно з мовними стандартами",
        ["Language_SamplePreview"] = "Зразок дати",

        // Storage & Cache Section
        ["Storage_SectionTitle"] = "Пам'ять та кеш",
        ["Storage_SectionSubtitle"] = "Керування дисковим простором, тимчасовими файлами та кешем метаданих",
        ["Storage_ThumbnailsTitle"] = "Кеш ескізів",
        ["Storage_ThumbnailsSubtitle"] = "Збережені на диску мініатюри відео для швидкого гортання списку",
        ["Storage_ThumbnailsButton"] = "Очистити ескізи",
        ["Storage_ClipCacheTitle"] = "Кеш метаданих кліпів",
        ["Storage_ClipCacheSubtitle"] = "Збережена тривалість, роздільна здатність і частота кадрів для миттєвого завантаження",
        ["Storage_ClipCacheButton"] = "Очистити кеш",
        ["Storage_MomentsTitle"] = "Метадані збережених моментів",
        ["Storage_MomentsSubtitle"] = "Очистити всі збережені закладки-моменти та позначки хайлайтів із ваших кліпів",
        ["Storage_MomentsButton"] = "Очистити моменти",
        ["Storage_Status_MomentsCleared"] = "Метадані збережених моментів очищено (видалено {0} моментів)",
        ["Storage_Status_NoMoments"] = "Збережених моментів не знайдено",

        // Updates Section
        ["Updates_SectionTitle"] = "Оновлення",
        ["Updates_SectionSubtitle"] = "Оновлюйте Reeled для отримання найновіших функцій, покращень та виправлень",
        ["Updates_StatusTitle"] = "Reeled оновлено до останньої версії",
        ["Updates_StatusChecking"] = "Перевірка наявності оновлень...",
        ["Updates_StatusAvailable"] = "Доступне оновлення",
        ["Updates_StatusDownloading"] = "Завантаження оновлення...",
        ["Updates_StatusReady"] = "Готово до встановлення",
        ["Updates_CheckButton"] = "Перевірити наявність оновлень",
        ["Updates_CheckingButton"] = "Перевірка...",
        ["Updates_LastChecked"] = "Остання перевірка: {0}",
        ["Updates_VersionBadge"] = "v1.0.0 (x64)",
        ["Updates_AutoCheckTitle"] = "Перевіряти оновлення автоматично",
        ["Updates_AutoCheckSubtitle"] = "Періодично перевіряти нові релізи у фоновому режимі",
        ["Updates_AvailableHeader"] = "Оновлення Reeled",
        ["Updates_AvailableNotes"] = "Включає українську локалізацію, оновлені категорії налаштувань та оптимізацію швидкодії.",
        ["Updates_DownloadButton"] = "Завантажити та встановити",
        ["Updates_InstallButton"] = "Встановити та перезапустити",
        ["Updates_Size"] = "Розмір: 38.4 МБ",

        // About Section
        ["About_Tagline"] = "Сучасний переглядач та плеєр ігрових кліпів для Windows",
        ["About_Version"] = "Версія 1.0.0",
        ["About_Framework"] = "Створено на WinUI 3, Windows App SDK та LibVLC",
        ["About_Developer"] = "Зроблено з любов'ю для геймерів та авторів кліпів",
        ["About_GitHub"] = "Репозиторій GitHub",
        ["About_Releases"] = "Список змін",
        ["About_Issues"] = "Повідомити про проблему",
        ["About_License"] = "Ліцензія MIT • Відкритий сирцевий код"
    };
}
