using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface IClipMetadataCacheService
{
    bool TryGet(string filePath, long length, long lastWriteTimeTicks, out CachedClipMetadata? cached);
    void Set(CachedClipMetadata metadata);
    Task SaveAsync();
    /// <summary>Synchronous save used during shutdown.</summary>
    void Flush();
    Task ClearAsync();
    (int count, long bytes) GetCacheStats();
}
