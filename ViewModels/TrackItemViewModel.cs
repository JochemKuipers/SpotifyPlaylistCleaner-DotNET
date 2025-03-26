using System;
using System.Collections.Generic;
using ReactiveUI;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class TrackItemViewModel(FullTrack track, int index) : ViewModelBase
{
    private int _displayIndex = index;

    private FullTrack Track { get; } = track;

    public int DisplayIndex
    {
        get => _displayIndex;
        set => this.RaiseAndSetIfChanged(ref _displayIndex, value);
    }

    public string? Name => Track.Name;
    public IList<SimpleArtist> Artists => Track.Artists;
    public SimpleAlbum Album => Track.Album;
    public string? Duration => TimeSpan.FromMilliseconds(Track.DurationMs).ToString(@"hh\:mm\:ss");
    public bool IsPlayable => Track.IsPlayable;
    public bool Explicit => Track.Explicit;
    public bool IsLocal => Track.IsLocal;
}