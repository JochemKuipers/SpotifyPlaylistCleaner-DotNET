using System.Collections.Generic;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public interface ITreeNode
{
    string DisplayName { get; }
    string DisplayArtist { get; }
    string DisplayImage { get; }
}