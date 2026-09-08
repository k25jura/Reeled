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
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _directoryPath = string.Empty;

    [ObservableProperty]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
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

    public ObservableCollection<ClipBookmark> Bookmarks { get; set; } = new();

    public GameClip()
    {
        Bookmarks.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(BookmarkCount));
            OnPropertyChanged(nameof(HasBookmarks));
            OnPropertyChanged(nameof(FormattedBookmarkCount));
        };
    }

    public int BookmarkCount => Bookmarks.Count;
    public bool HasBookmarks => Bookmarks.Count > 0;
    public string FormattedBookmarkCount =>
        $"{Bookmarks.Count} {(Bookmarks.Count == 1 ? "moment" : "moments")}";

    public string FormattedDuration =>
        Duration.Hours > 0
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");

    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            double kb = FileSizeBytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F1} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
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
}
