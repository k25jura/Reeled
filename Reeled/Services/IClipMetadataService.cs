using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface IClipMetadataService
{
    Task PopulateMetadataAsync(GameClip clip);

    /// <summary>The clip's frame rate (frames per second), or 0 when it can't be read. Cached with the other metadata.</summary>
    Task<double> GetFramerateAsync(GameClip clip);
}
