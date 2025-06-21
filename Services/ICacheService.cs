using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public interface ICacheService
{
    Task<IList<FullTrack>?> LoadTracksFromCacheAsync(string playlistId, CancellationToken cancellationToken = default);
    Task<IList<FullTrack>?> LoadLikedTracksFromCacheAsync(CancellationToken cancellationToken = default);
    Task SaveTracksToCacheAsync(string playlistId, IList<FullTrack> tracks, CancellationToken cancellationToken = default);
    Task SaveLikedTracksToCacheAsync(IList<FullTrack> tracks, CancellationToken cancellationToken = default);
    void ClearCache();
    bool IsCacheEnabled { get; set; }
}