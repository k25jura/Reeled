using System;
using System.Collections.Generic;
using System.Linq;
using Reeled.Models;

namespace Reeled.ViewModels;

/// <summary>
/// Tags: user labels on clips ("clutch", "funny", "send to Max"), plus each clip's game (its folder)
/// as an automatic filter. Filtering shows clips matching any selected tag or game.
/// </summary>
public partial class HomeViewModel
{
    private readonly HashSet<string> _tagFilters = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _gameFilters = new(StringComparer.CurrentCultureIgnoreCase);

    public event Action? TagsChanged;

    public IReadOnlyCollection<string> ActiveTagFilters => _tagFilters;
    public IReadOnlyCollection<string> ActiveGameFilters => _gameFilters;
    public int ActiveFilterCount => _tagFilters.Count + _gameFilters.Count;

    /// <summary>The user's tags in their order.</summary>
    public IReadOnlyList<string> TagLibrary => _storageService.CurrentSettings.TagLibrary;

    public int CountWithTag(string tag) => AllClips.Count(c => c.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase));

    /// <summary>Games (clip folders) with clip counts, most clips first.</summary>
    public IReadOnlyList<(string Game, int Count)> GamesWithCounts() => AllClips
        .Where(c => !string.IsNullOrEmpty(c.FolderName))
        .GroupBy(c => c.FolderName, StringComparer.CurrentCultureIgnoreCase)
        .Select(g => (g.First().FolderName, g.Count()))
        .OrderByDescending(g => g.Item2)
        .ToList();

    private static void ApplyPersistedTags(GameClip clip, AppSettings settings)
    {
        if (settings.ClipTags.TryGetValue(clip.FilePath, out var tags) && tags.Count > 0)
        {
            clip.Tags = SortTags(tags, settings.TagLibrary);
        }
    }

    private static IReadOnlyList<string> SortTags(IEnumerable<string> tags, List<string> library) => tags
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(t =>
        {
            int i = library.FindIndex(l => string.Equals(l, t, StringComparison.CurrentCultureIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        })
        .ToList();

    /// <summary>Adds a tag to the library (if new) and returns its canonical spelling.</summary>
    public string CreateTag(string name)
    {
        name = name.Trim();
        var library = _storageService.CurrentSettings.TagLibrary;
        var existing = library.FirstOrDefault(t => string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null) return existing;
        library.Add(name);
        _ = _storageService.SaveSettingsAsync(_storageService.CurrentSettings);
        TagsChanged?.Invoke();
        return name;
    }

    public void SetClipTag(GameClip clip, string tag, bool on)
    {
        var settings = _storageService.CurrentSettings;
        var tags = clip.Tags.ToList();
        bool has = tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase);
        if (on == has) return;
        if (on) tags.Add(tag);
        else tags.RemoveAll(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase));

        clip.Tags = SortTags(tags, settings.TagLibrary);
        if (tags.Count == 0) settings.ClipTags.Remove(clip.FilePath);
        else settings.ClipTags[clip.FilePath] = clip.Tags.ToList();
        _ = _storageService.SaveSettingsAsync(settings);

        if (ActiveFilterCount > 0) ApplyFilterAndSort();
        TagsChanged?.Invoke();
    }

    public void RenameTag(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || string.Equals(oldName, newName, StringComparison.CurrentCulture)) return;
        var settings = _storageService.CurrentSettings;
        int i = settings.TagLibrary.FindIndex(t => string.Equals(t, oldName, StringComparison.CurrentCultureIgnoreCase));
        if (i >= 0) settings.TagLibrary[i] = newName;
        foreach (var clip in AllClips.Where(c => c.Tags.Contains(oldName, StringComparer.CurrentCultureIgnoreCase)))
        {
            var tags = clip.Tags.Select(t => string.Equals(t, oldName, StringComparison.CurrentCultureIgnoreCase) ? newName : t).ToList();
            clip.Tags = SortTags(tags, settings.TagLibrary);
            settings.ClipTags[clip.FilePath] = clip.Tags.ToList();
        }
        if (_tagFilters.Remove(oldName)) _tagFilters.Add(newName);
        _ = _storageService.SaveSettingsAsync(settings);
        ApplyFilterAndSort();
        TagsChanged?.Invoke();
    }

    public void DeleteTag(string name)
    {
        var settings = _storageService.CurrentSettings;
        settings.TagLibrary.RemoveAll(t => string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var clip in AllClips.Where(c => c.Tags.Contains(name, StringComparer.CurrentCultureIgnoreCase)))
        {
            var tags = clip.Tags.Where(t => !string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
            clip.Tags = tags;
            if (tags.Count == 0) settings.ClipTags.Remove(clip.FilePath);
            else settings.ClipTags[clip.FilePath] = tags;
        }
        _tagFilters.Remove(name);
        _ = _storageService.SaveSettingsAsync(settings);
        ApplyFilterAndSort();
        TagsChanged?.Invoke();
    }

    public void SetTagFilter(string tag, bool on)
    {
        if (on) _tagFilters.Add(tag); else _tagFilters.Remove(tag);
        ApplyFilterAndSort();
        OnPropertyChanged(nameof(ActiveFilterCount));
        RequestScrollToTop?.Invoke();
    }

    public void SetGameFilter(string game, bool on)
    {
        if (on) _gameFilters.Add(game); else _gameFilters.Remove(game);
        ApplyFilterAndSort();
        OnPropertyChanged(nameof(ActiveFilterCount));
        RequestScrollToTop?.Invoke();
    }

    public void ClearFilters()
    {
        if (ActiveFilterCount == 0) return;
        _tagFilters.Clear();
        _gameFilters.Clear();
        ApplyFilterAndSort();
        OnPropertyChanged(nameof(ActiveFilterCount));
    }

    private IEnumerable<GameClip> ApplyTagFilters(IEnumerable<GameClip> query)
    {
        if (ActiveFilterCount == 0) return query;
        return query.Where(c =>
            (_tagFilters.Count > 0 && c.Tags.Any(t => _tagFilters.Contains(t))) ||
            (_gameFilters.Count > 0 && _gameFilters.Contains(c.FolderName)));
    }
}
