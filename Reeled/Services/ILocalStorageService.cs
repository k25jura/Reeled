using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface ILocalStorageService
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    AppSettings CurrentSettings { get; }
    void SaveSettingsSync(AppSettings settings);
}
