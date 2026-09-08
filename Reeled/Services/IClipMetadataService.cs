using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface IClipMetadataService
{
    Task PopulateMetadataAsync(GameClip clip);
}
