using System.Collections.Generic;
using System.Linq;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class DuplicateGroup : ITreeNode
{
    public List<TrackModel> Tracks { get; set; } = [];
    public int DuplicateCount => Tracks.Count;
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayArtist { get; set; } = string.Empty;
    public string DisplayImage { get; set; } = string.Empty;
}