using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Reeled.Services;

public interface IThumbnailService
{
    Task<BitmapImage?> GetThumbnailAsync(string videoPath, bool highPriority = false);
    bool TryGetFromMemoryCache(string videoPath, out BitmapImage? bitmap);
    Task<string?> EnsureThumbnailOnDiskAsync(string videoPath, bool highPriority = false);
    void ClearMemoryCache();
    /// <summary>Holds the library-wide background extraction (not on-screen requests) while true.</summary>
    void SetBackgroundPaused(bool paused);
}
