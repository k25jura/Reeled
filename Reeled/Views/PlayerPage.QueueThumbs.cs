using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>
/// Thumbnails for the clip queue. They share the library's thumbnail service (same disk file, same
/// 360px decode, same bounded memory cache) but are loaded on their own lane: a few at a time, in the
/// order the rows were realized (visible rows first), skipping rows that were recycled while waiting.
/// The image is set directly rather than through <see cref="GameClip.Thumbnail"/>, which the library
/// clears whenever its own card scrolls away.
/// </summary>
public sealed partial class PlayerPage
{
    private const int QueueThumbConcurrency = 3;

    private readonly Queue<(Image Image, string Path)> _queueThumbPending = new();
    private int _queueThumbActive;

    private void OnPlaylistContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var image = FindQueueThumbImage(args.ItemContainer);
        if (image == null) return;

        if (args.InRecycleQueue)
        {
            // Nothing in flight for this row matters any more (the pump checks the tag before applying)
            image.Tag = null;
            image.Source = null;
            image.Opacity = 0;
            return;
        }

        if (args.Item is not GameClip clip) return;

        string path = clip.FilePath;
        if (Equals(image.Tag, path) && image.Source != null) return;

        image.Tag = path;
        image.Source = null;
        image.Opacity = 0;

        var service = App.GetService<Services.IThumbnailService>();
        if (service == null) return;

        // Already decoded for the library: no wait, no fade
        if (service.TryGetFromMemoryCache(path, out var cached) && cached != null)
        {
            image.Source = cached;
            image.Opacity = 1;
            return;
        }

        _queueThumbPending.Enqueue((image, path));
        PumpQueueThumbs();
    }

    /// <summary>The thumbnail Image of a queue row (template: drag handle, thumbnail frame, title, duration).</summary>
    private static Image? FindQueueThumbImage(SelectorItem? container)
    {
        if (container?.ContentTemplateRoot is not Grid root) return null;
        foreach (var child in root.Children)
        {
            if (child is Grid frame && frame.Children.Count > 0 && frame.Children[0] is Image image) return image;
        }
        return null;
    }

    private void PumpQueueThumbs()
    {
        while (_queueThumbActive < QueueThumbConcurrency && _queueThumbPending.Count > 0)
        {
            var (image, path) = _queueThumbPending.Dequeue();
            // Recycled or re-bound to another clip while it waited
            if (!Equals(image.Tag, path) || image.Source != null) continue;

            _queueThumbActive++;
            _ = LoadQueueThumbAsync(image, path);
        }
    }

    private async System.Threading.Tasks.Task LoadQueueThumbAsync(Image image, string path)
    {
        try
        {
            var service = App.GetService<Services.IThumbnailService>();
            if (service == null) return;

            var bitmap = await System.Threading.Tasks.Task.Run(() => service.GetThumbnailAsync(path, highPriority: true));

            if (bitmap == null || !Equals(image.Tag, path) || image.Source != null) return;
            image.Source = bitmap;

            if (Helpers.Motion.IsReduced)
            {
                image.Opacity = 1;
            }
            else
            {
                var sb = new Storyboard();
                sb.Children.Add(Helpers.Motion.Spline(image, "Opacity", 1, Helpers.Motion.Medium, from: 0));
                Helpers.Motion.BeginExact(sb);
            }
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("QueueThumbnail", ex);
        }
        finally
        {
            _queueThumbActive--;
            PumpQueueThumbs();
        }
    }
}
