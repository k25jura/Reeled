using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>A game found at an icon provider. The thumbnail may be filled in later (see ResolveThumbnailAsync).</summary>
public sealed record GameIconResult(IconProvider Provider, int Id, string Name, string? ThumbnailUrl);

/// <summary>A place to find game icons: search by name, then download the chosen game's icon.</summary>
public interface IGameIconProvider
{
    IconProvider Id { get; }
    string DisplayName { get; }

    /// <summary>False until the provider's key is entered in Settings (Steam needs none).</summary>
    bool IsConfigured { get; }

    Task<List<GameIconResult>> SearchAsync(string term, CancellationToken ct);

    /// <summary>
    /// Downloads the game's icon into the cache and returns its path, or null when the provider has
    /// nothing usable for it. Network errors may throw.
    /// </summary>
    Task<string?> DownloadAsync(GameIconResult game, CancellationToken ct);

    /// <summary>A preview image for results that came without one (null when there is none).</summary>
    Task<string?> ResolveThumbnailAsync(GameIconResult game, CancellationToken ct) => Task.FromResult(game.ThumbnailUrl);
}
