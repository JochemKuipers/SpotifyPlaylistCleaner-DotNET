using System.Collections.Generic;
using System.Linq;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class DuplicateGroup : ITreeNode
{
    public List<TrackModel> Tracks { get; init; } = [];
    public int DuplicateCount => Tracks.Count;
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayArtist { get; init; } = string.Empty;
    public string DisplayImage { get; init; } = string.Empty;
}