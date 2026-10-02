using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Reeled.Models;

public partial class GameClip : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string? _customTitle;

    public string DisplayName => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : FileName;

    [ObservableProperty]
    private string _directoryPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedFileSize))]
    private long _fileSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDate))]
    [NotifyPropertyChangedFor(nameof(EffectiveDate))]
    private DateTime _createdDate = DateTime.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDate))]
    [NotifyPropertyChangedFor(nameof(EffectiveDate))]
    private DateTime _modifiedDate = DateTime.MinValue;

    public DateTime EffectiveDate => ModifiedDate != DateTime.MinValue ? ModifiedDate : CreatedDate;

    [ObservableProperty]
    private uint _videoWidth;

    [ObservableProperty]
    private uint _videoHeight;

    [ObservableProperty]
    private double _framerate;

    [ObservableProperty]
    private string _codec = string.Empty;

    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private string? _thumbnailCachePath;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<ClipBookmark> Bookmarks { get; set; } = new();

    public GameClip()
    {
        Bookmarks.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(BookmarkCount));
            OnPropertyChanged(nameof(HasBookmarks));
            OnPropertyChanged(nameof(ShowBookmarkBadge));
            OnPropertyChanged(nameof(FormattedBookmarkCount));
        };
    }

    public int BookmarkCount => Bookmarks.Count;
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool ShowBookmarkBadge
    {
        get
        {
            try
            {
                var storage = App.GetService<Services.ILocalStorageService>();
                bool enabled = storage?.CurrentSettings?.ShowMomentsBadges ?? true;
                return HasBookmarks && enabled;
            }
            catch
            {
                return HasBookmarks;
            }
        }
    }
    public string FormattedBookmarkCount
    {
        get
        {
            try
            {
                var loc = App.GetService<Services.ILocalizationService>();
                if (loc != null)
                {
                    return $"{Bookmarks.Count} {loc.FormatPlural("Plural_Moment", Bookmarks.Count)}";
                }
            }
            catch { }
            return $"{Bookmarks.Count} {(Bookmarks.Count == 1 ? "moment" : "moments")}";
        }
    }

    public void RefreshFormattedStrings()
    {
        OnPropertyChanged(nameof(FormattedDate));
        OnPropertyChanged(nameof(FormattedBookmarkCount));
        OnPropertyChanged(nameof(ShowBookmarkBadge));
        OnPropertyChanged(nameof(FormattedFileSize));
    }

    public void NotifyMomentsBadgeChanged() => OnPropertyChanged(nameof(ShowBookmarkBadge));

    public string FormattedDuration =>
        Duration.Hours > 0
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");

    public string FormattedFileSize
    {
        get
        {
            var loc = App.GetService<Services.ILocalizationService>();
            string bUnit = loc?["Unit_Byte"] ?? "B";
            string kbUnit = loc?["Unit_KB"] ?? "KB";
            string mbUnit = loc?["Unit_MB"] ?? "MB";
            string gbUnit = loc?["Unit_GB"] ?? "GB";
            var culture = loc?.CurrentCulture ?? System.Globalization.CultureInfo.InvariantCulture;

            if (FileSizeBytes < 1024) return string.Format(culture, "{0} {1}", FileSizeBytes, bUnit);
            double kb = FileSizeBytes / 1024.0;
            if (kb < 1024) return string.Format(culture, "{0:F1} {1}", kb, kbUnit);
            double mb = kb / 1024.0;
            if (mb < 1024) return string.Format(culture, "{0:F1} {1}", mb, mbUnit);
            double gb = mb / 1024.0;
            return string.Format(culture, "{0:F2} {1}", gb, gbUnit);
        }
    }

    public string FormattedDate
    {
        get
        {
            var dt = EffectiveDate;
            if (dt == DateTime.MinValue) return string.Empty;

            var local = dt.ToLocalTime();
            var diff = DateTime.Now.Date - local.Date;

            try
            {
                var loc = App.GetService<Services.ILocalizationService>();
                if (loc != null)
                {
                    var culture = loc.CurrentCulture;
                    string todayStr = loc.GetString("Date_Today");
                    string yesterdayStr = loc.GetString("Date_Yesterday");

                    if (diff.TotalDays == 0)
                        return $"{todayStr} {local:HH:mm}";
                    if (diff.TotalDays == 1)
                        return $"{yesterdayStr} {local:HH:mm}";
                    if (diff.TotalDays < 7)
                        return local.ToString("ddd HH:mm", culture);

                    return loc.EffectiveLanguage == "uk"
                        ? local.ToString("dd.MM.yyyy HH:mm", culture)
                        : local.ToString("yyyy-MM-dd HH:mm", culture);
                }
            }
            catch { }

            if (diff.TotalDays == 0)
                return $"Today {local:HH:mm}";
            if (diff.TotalDays == 1)
                return $"Yesterday {local:HH:mm}";
            if (diff.TotalDays < 7)
                return $"{local:ddd HH:mm}";

            return local.ToString("yyyy-MM-dd HH:mm");
        }
    }

    public string FormattedQuality
    {
        get
        {
            if (VideoWidth == 0 || VideoHeight == 0)
                return Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();

            string res = VideoHeight switch
            {
                >= 2160 => "4K",
                >= 1440 => "1440p",
                >= 1080 => "1080p",
                >= 720 => "720p",
                _ => $"{VideoHeight}p"
            };

            if (Framerate >= 55)
            {
                return $"{res} {Math.Round(Framerate)}fps";
            }

            return res;
        }
    }

    public override bool Equals(object? obj) =>
        obj is GameClip other && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        string.IsNullOrEmpty(FilePath) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
}
