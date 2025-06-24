using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public class SpotifyService(SpotifyClient? spotifyClient, IAuthenticationService authService) : ISpotifyService
{
    private const int MaxConcurrentRequests = 25;
    private const int ProgressThreshold = 5;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private SpotifyClient? _spotifyClient = spotifyClient;

    private async Task<SpotifyClient> GetOrInitializeClient()
    {
        if (_spotifyClient != null)
            return _spotifyClient;

        await _initializationLock.WaitAsync();
        try
        {
            if (_spotifyClient != null)
                return _spotifyClient;

            _spotifyClient = await authService.Authenticate();
            return _spotifyClient;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<SpotifyClient> GetClient()
    {
        return await GetOrInitializeClient();
    }

    public async Task<PrivateUser> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        return await client.UserProfile.Current(cancellationToken);
    }

    public async Task<IList<FullPlaylist>> GetUserPlaylists(CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var user = await client.UserProfile.Current(cancellationToken);
        var tempPlaylists = new List<FullPlaylist>();

        var likedSongsPlaylist = new FullPlaylist
        {
            Id = "liked_songs_virtual",
            Name = "Liked Songs",
            Description = "Songs you've liked on Spotify",
            Images = [new Image { Url = "https://t.scdn.co/images/3099b3803ad9496896c43f22fe9be8c4.png" }],
            Owner = new PublicUser { Id = user.Id }
        };
        tempPlaylists.Add(likedSongsPlaylist);

        var playlistsResponse = await client.Playlists.CurrentUsers(cancel: cancellationToken);

        tempPlaylists.AddRange(playlistsResponse.Items!.Where(playlist => playlist.Owner?.Id == user.Id));

        while (playlistsResponse.Next != null)
        {
            playlistsResponse = await client.NextPage(playlistsResponse);
            foreach (var playlist in playlistsResponse.Items!.Where(p => p.Owner?.Id == user.Id))
                tempPlaylists.Add(playlist);
        }

        return tempPlaylists;
    }

    private readonly Dictionary<string, Dictionary<string, int>> _trackPositions = new();

    public async Task<IList<FullTrack>> GetPlaylistTracks(string playlistId, IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var user = await client.UserProfile.Current(cancellationToken);
        var allTracks = new List<FullTrack>();
        var positionMap = new Dictionary<string, int>(); // Maps track URIs to their positions in the playlist

        var initialRequest = new PlaylistGetItemsRequest
        {
            Limit = 100,
            Market = user.Country
        };

        var initialResponse = await client.Playlists.GetItems(playlistId, initialRequest, cancellationToken);
        var totalTracks = initialResponse.Total ?? 0;

        if (totalTracks == 0)
            return allTracks;

        // Store positions for the first batch
        for (var i = 0; i < initialResponse.Items!.Count; i++)
        {
            var item = initialResponse.Items[i];
            if (item.Track is not FullTrack track) continue;
            allTracks.Add(track);
            // Store the position for each track, especially important for local tracks
            positionMap[track.Uri] = i;
        }

        var loadedCount = initialResponse.Items!.Count;
        progress?.Report((loadedCount, totalTracks));

        var tasks = new List<Task>();
        var progressLock = new object();
        var lastReportedProgress = (int)(loadedCount * 100.0 / totalTracks);

        for (var offset = 100; offset < totalTracks; offset += 100)
        {
            if (tasks.Count >= MaxConcurrentRequests)
            {
                await Task.WhenAny(tasks);
                tasks.RemoveAll(t => t.IsCompleted);
            }

            var currentOffset = offset;
            var task = Task.Run(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var request = new PlaylistGetItemsRequest
                    {
                        Market = user.Country,
                        Limit = 100,
                        Offset = currentOffset
                    };

                    var response = await client.Playlists.GetItems(playlistId, request, cancellationToken);

                    var batchTracks = new List<FullTrack>();
                    // Store positions for each batch
                    for (var i = 0; i < response.Items!.Count; i++)
                    {
                        var item = response.Items[i];
                        if (item.Track is not FullTrack track) continue;
                        batchTracks.Add(track);
                        // Store the position with the currentOffset
                        var position = currentOffset + i;
                        lock (progressLock)
                        {
                            positionMap[track.Uri] = position;
                        }
                    }

                    lock (progressLock)
                    {
                        allTracks.AddRange(batchTracks);
                        loadedCount += batchTracks.Count;

                        var currentBatchProgress = (int)(loadedCount * 100.0 / totalTracks);
                        if (currentBatchProgress - lastReportedProgress >= ProgressThreshold || currentBatchProgress == 100)
                        {
                            lastReportedProgress = currentBatchProgress;
                            progress?.Report((loadedCount, totalTracks));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Error fetching batch at offset {currentOffset}: {ex.Message}");
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Set the positionMap as a property so it can be accessed by other methods
        _trackPositions[playlistId] = positionMap;

        return allTracks;
    }

    public async Task<IList<FullTrack>> GetLikedTracks(IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var user = await client.UserProfile.Current(cancellationToken);
        var allTracks = new List<FullTrack>();

        var initialRequest = new LibraryTracksRequest { Limit = 50, Market = user.Country };
        var initialResponse = await client.Library.GetTracks(initialRequest, cancellationToken);
        var totalTracks = initialResponse.Total ?? 0;

        if (totalTracks == 0)
            return allTracks;

        foreach (var item in initialResponse.Items!)
            if (item.Track is { } track)
                allTracks.Add(track);

        var loadedCount = initialResponse.Items!.Count;
        progress?.Report((loadedCount, totalTracks));

        var tasks = new List<Task>();
        var progressLock = new object();
        var lastReportedProgress = (int)(loadedCount * 100.0 / totalTracks);

        for (var offset = 50; offset < totalTracks; offset += 50)
        {
            if (tasks.Count >= MaxConcurrentRequests)
            {
                await Task.WhenAny(tasks);
                tasks.RemoveAll(t => t.IsCompleted);
            }

            var currentOffset = offset;
            var task = Task.Run(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var request = new LibraryTracksRequest
                    {
                        Market = user.Country,
                        Limit = 50,
                        Offset = currentOffset
                    };

                    var response = await client.Library.GetTracks(request, cancellationToken);

                    var batchTracks = new List<FullTrack>();
                    foreach (var item in response.Items!)
                        if (item.Track is { } track)
                            batchTracks.Add(track);

                    lock (progressLock)
                    {
                        allTracks.AddRange(batchTracks);
                        loadedCount += batchTracks.Count;

                        var currentBatchProgress = (int)(loadedCount * 100.0 / totalTracks);
                        if (currentBatchProgress - lastReportedProgress >= ProgressThreshold || currentBatchProgress == 100)
                        {
                            lastReportedProgress = currentBatchProgress;
                            progress?.Report((loadedCount, totalTracks));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Error fetching batch at offset {currentOffset}: {ex.Message}");
                }
                catch (APIException ex)
                {
                    Console.WriteLine($"API error fetching batch at offset {currentOffset}: {ex.Message}");
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        return allTracks;
    }

    public async Task RemoveTrackFromPlaylist(string playlistId, string trackUri, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var playlist = await client.Playlists.Get(playlistId, cancellationToken);
        var snapshotId = playlist.SnapshotId;

        // Check if this is a local track
        if (trackUri.StartsWith("spotify:local:"))
        {
            // Look up the position from our cached positions
            if (_trackPositions.TryGetValue(playlistId, out var positions) && positions.TryGetValue(trackUri, out var position))
            {
                // We have the position cached, so we can remove it directly
                await client.Playlists.RemoveItems(
                    playlistId,
                    new PlaylistRemoveItemsRequest
                    {
                        Positions = [position],
                        SnapshotId = snapshotId
                    },
                    cancellationToken);
                Console.WriteLine($"Successfully removed local track at position {position}");

                // Update the positions for tracks that come after the removed track
                if (positions.Count > 0)
                {
                    var keysToUpdate = positions.Keys.Where(k => positions[k] > position).ToList();
                    foreach (var key in keysToUpdate)
                    {
                        positions[key]--;
                    }
                    // Remove the track from our positions dictionary
                    positions.Remove(trackUri);
                }
            }
            else
            {
                // If we don't have the position cached, we need to find it
                var playlistItems = await client.Playlists.GetItems(playlistId, cancel: cancellationToken);
                var totalTracks = playlistItems.Total ?? 0;

                // Find the position of the local track
                var pos = -1;
                for (var i = 0; i < playlistItems.Items!.Count; i++)
                {
                    var item = playlistItems.Items[i];
                    if (item.Track is not FullTrack track || track.Uri != trackUri) continue;
                    pos = i;
                    break;
                }

                // If we didn't find it in the first page, check subsequent pages
                var offset = playlistItems.Items!.Count;
                while (pos == -1 && offset < totalTracks)
                {
                    var request = new PlaylistGetItemsRequest
                    {
                        Offset = offset,
                        Limit = 100
                    };

                    var response = await client.Playlists.GetItems(playlistId, request, cancellationToken);

                    for (var i = 0; i < response.Items!.Count; i++)
                    {
                        var item = response.Items[i];
                        if (item.Track is not FullTrack track || track.Uri != trackUri) continue;
                        pos = offset + i;
                        break;
                    }

                    offset += response.Items!.Count;
                }

                if (pos >= 0)
                {
                    // Remove the track by position
                    await client.Playlists.RemoveItems(
                        playlistId,
                        new PlaylistRemoveItemsRequest
                        {
                            Positions = [pos],
                            SnapshotId = snapshotId
                        },
                        cancellationToken);
                    Console.WriteLine($"Successfully removed local track at position {pos}");
                }
                else
                {
                    Console.WriteLine($"Could not find local track: {trackUri}");
                }
            }
        }
        else
        {
            // For regular Spotify tracks, use the standard URI-based removal
            await client.Playlists.RemoveItems(
                playlistId,
                new PlaylistRemoveItemsRequest
                {
                    Tracks = [new PlaylistRemoveItemsRequest.Item { Uri = trackUri }],
                    SnapshotId = snapshotId
                },
                cancellationToken);

            // Update our cached positions if we have them
            if (_trackPositions.TryGetValue(playlistId, out var positions) && positions.TryGetValue(trackUri, out var position))
            {
                // Update the positions for tracks that come after the removed track
                var keysToUpdate = positions.Keys.Where(k => positions[k] > position).ToList();
                foreach (var key in keysToUpdate)
                {
                    positions[key]--;
                }
                // Remove the track from our positions dictionary
                positions.Remove(trackUri);
            }
        }
    }

    public async Task RemoveTrackFromLikedSongs(string trackId, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        await client.Library.RemoveTracks(
            new LibraryRemoveTracksRequest([trackId]),
            cancellationToken);
    }
}