using System;
using System.Collections.Generic;
using System.Linq;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class TrackModel : ITreeNode
{
    public string Id { get; private init; } = string.Empty;
    public string Uri { get; private init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Artists { get; set; } = [];
    public string ArtistNames => string.Join(", ", Artists);
    public string Album { get; private init; } = string.Empty;
    public string AlbumImageUrl { get; set; } = string.Empty;
    public int DurationMs { get; private init; }
    public string Duration => TimeSpan.FromMilliseconds(DurationMs).ToString(@"mm\:ss");
    public bool IsPlayable { get; private init; }
    public bool IsExplicit { get; set; }
    public bool IsLocal { get; set; }
    public int DisplayIndex { get; set; }

    // ITreeNode implementation
    string ITreeNode.DisplayName => Name;
    string ITreeNode.DisplayArtist => ArtistNames;
    string ITreeNode.DisplayImage => AlbumImageUrl;

    public static TrackModel FromFullTrack(FullTrack track, int index)
    {
        return new TrackModel
        {
            Id = track.Id,
            Uri = track.Uri,
            Name = track.Name,
            Artists = [.. track.Artists.Select(a => a.Name)],
            Album = track.Album.Name,
            AlbumImageUrl = track.Album.Images.FirstOrDefault()?.Url ?? string.Empty,
            DurationMs = track.DurationMs,
            IsPlayable = track.IsPlayable,
            IsExplicit = track.Explicit,
            IsLocal = track.IsLocal,
            DisplayIndex = index
        };
    }
}