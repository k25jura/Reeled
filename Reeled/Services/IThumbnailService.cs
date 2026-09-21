using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Reeled.Services;

public interface IThumbnailService
{
    Task<BitmapImage?> GetThumbnailAsync(string videoPath);
    bool TryGetFromMemoryCache(string videoPath, out BitmapImage? bitmap);
    Task<string?> EnsureThumbnailOnDiskAsync(string videoPath);
    void ClearMemoryCache();
}
