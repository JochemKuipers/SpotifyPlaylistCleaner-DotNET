using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public interface ISpotifyService
{
    Task<SpotifyClient> GetClient();
    Task<PrivateUser> GetCurrentUser(CancellationToken cancellationToken = default);
    Task<IList<FullPlaylist>> GetUserPlaylists(CancellationToken cancellationToken = default);
    Task<IList<FullTrack>> GetPlaylistTracks(string playlistId, IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default);
    Task<IList<FullTrack>> GetLikedTracks(IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default);
    Task RemoveTrackFromPlaylist(string playlistId, string trackUri, CancellationToken cancellationToken = default);
    Task RemoveTrackFromLikedSongs(string trackId, CancellationToken cancellationToken = default);
}