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
            tempPlaylists.AddRange(playlistsResponse.Items!.Where(p => p.Owner?.Id == user.Id));
        }

        return tempPlaylists;
    }

    public async Task<IList<FullTrack>> GetPlaylistTracks(string playlistId, IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var allTracks = new List<FullTrack>();

        var initialRequest = new PlaylistGetItemsRequest
        {
            Limit = 100,
        };

        var initialResponse = await client.Playlists.GetPlaylistItems(playlistId, initialRequest, cancellationToken);
        var totalTracks = initialResponse.Total ?? 0;

        if (totalTracks == 0)
            return allTracks;

        foreach (var item in initialResponse.Items!)
        {
            if (item.Track is FullTrack track)
                allTracks.Add(track);
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
                        Limit = 100,
                        Offset = currentOffset
                    };

                    var response = await client.Playlists.GetPlaylistItems(playlistId, request, cancellationToken);

                    var batchTracks = new List<FullTrack>();
                    foreach (var item in response.Items!)
                    {
                        if (item.Track is FullTrack track)
                            batchTracks.Add(track);
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


        return allTracks;
    }

    public async Task<IList<FullTrack>> GetLikedTracks(IProgress<(int Loaded, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var allTracks = new List<FullTrack>();

        var initialRequest = new LibraryTracksRequest { Limit = 50 };
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

        await client.Playlists.RemovePlaylistItems(
            playlistId,
            new PlaylistRemoveItemsRequestV2
            {
                Items = [new PlaylistRemoveItemsRequestV2.Item { Uri = trackUri }],
                SnapshotId = snapshotId
            },
            cancellationToken);
    }

    public async Task RemoveTracksFromPlaylist(string playlistId, List<string> trackUris,
        CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        var playlist = await client.Playlists.Get(playlistId, cancellationToken);
        var snapshotId = playlist.SnapshotId;
        
        await client.Playlists.RemovePlaylistItems(
            playlistId,
            new PlaylistRemoveItemsRequestV2
            {
                Items = trackUris.Select(uri => new PlaylistRemoveItemsRequestV2.Item { Uri = uri }).ToList(),
                SnapshotId = snapshotId
            },
            cancellationToken);
    }

    public async Task RemoveTrackFromLikedSongs(string trackUri, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        await client.Library.RemoveItems(
            new LibraryRemoveItemsRequest([trackUri]),
            cancellationToken);
    }
    
    public async Task RemoveTracksFromLikedSongs(List<string> trackUris, CancellationToken cancellationToken = default)
    {
        var client = await GetOrInitializeClient();
        await client.Library.RemoveItems(
            new LibraryRemoveItemsRequest(trackUris.ToList()),
            cancellationToken);
    }
}