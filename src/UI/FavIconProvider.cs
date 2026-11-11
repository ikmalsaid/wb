using System;
using System.Drawing;

namespace wb.UI
{
    /// <summary>
    /// Interface for tab pages that provide a favicon and current URL for tab header rendering.
    /// </summary>
    public interface ITabFaviconProvider
    {
        Image Favicon { get; }
        string CurrentUrl { get; }
        string CurrentTitle { get; }
        bool IsLoading { get; }
    }

    /// <summary>
    /// Alias interface matching FavIconProvider naming convention.
    /// </summary>
    public interface IFavIconProvider : ITabFaviconProvider
    {
    }

    /// <summary>
    /// Interface for tab state (pinned status, audio playback, muting, loading).
    /// </summary>
    public interface IBrowserTabState : ITabFaviconProvider
    {
        bool IsPinned { get; }
        bool IsPlayingAudio { get; }
        bool IsAudioMuted { get; }
        bool IsMediaCaptured { get; }
        event EventHandler PinStateChanged;
        event EventHandler AudioStateChanged;
        event EventHandler MediaCaptureStateChanged;
        event EventHandler LoadingStateChanged;
    }
}
