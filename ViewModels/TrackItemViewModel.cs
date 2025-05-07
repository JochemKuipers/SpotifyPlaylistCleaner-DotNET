using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ReactiveUI;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class TrackItemViewModel(FullTrack track, int index, Action<FullTrack> deleteAction)
    : ViewModelBase, MainWindowViewModel.ITreeNode
{
    private int _displayIndex = index;
    internal FullTrack Track { get; } = track;
    public ICommand DeleteTrackCommand { get; } = ReactiveCommand.Create(() => deleteAction(track));

    public int DisplayIndex
    {
        get => _displayIndex;
        set => this.RaiseAndSetIfChanged(ref _displayIndex, value);
    }

    public string? Name => Track.Name;
    public IList<SimpleArtist> Artists => Track.Artists;
    private string? ArtistNames => string.Join(", ", Artists.Select(a => a.Name));

    public SimpleAlbum Album => Track.Album;
    public string Duration => TimeSpan.FromMilliseconds(Track.DurationMs).ToString(@"hh\:mm\:ss");
    public int DurationMs => Track.DurationMs;
    public bool IsPlayable => Track.IsPlayable;
    public bool Explicit => Track.Explicit;
    public bool IsLocal => Track.IsLocal;
    public string Uri => Track.Uri;
    public string? DisplayArtist => ArtistNames;
    public string? DisplayName => Name;
    public string? DisplayImage => Album.Images.FirstOrDefault()?.Url;
}