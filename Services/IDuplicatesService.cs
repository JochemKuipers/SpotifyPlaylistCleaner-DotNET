using System.Collections.Generic;
using SpotifyPlaylistCleaner_DotNET.Models;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public interface IDuplicatesService
{
    List<DuplicateGroup> FindDuplicates(IList<TrackModel> tracks);
    int GetTrackToKeepIndex(DuplicateGroup group);
}