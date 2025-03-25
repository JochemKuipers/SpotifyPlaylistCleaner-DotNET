using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    // Add these properties for cache management
    private readonly string _cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyPlaylistCleaner");

    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24); // Cache valid for 24 hours

    // Add this field to your class
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly ObservableCollection<TrackItemViewModel> _trackItems = [];

    // Add a CancellationTokenSource for managing API requests
    private CancellationTokenSource? _currentLoadingCts;
    private bool _isAuthenticated;
    private bool _isAuthenticating;
    private bool _isLoadingPlaylists;
    private bool _isLoadingTracks;
    private int _loadingProgress;
    private string _loadingStatusMessage = "";
    private ObservableCollection<FullPlaylist> _playlists = [];

    // Add these properties to your MainWindowViewModel class

    private string _searchQuery = "";
    private FullPlaylist? _selectedPlaylist;
    private SpotifyClient? _spotifyClient;
    private string _statusMessage = "";
    private ObservableCollection<FullTrack> _tracks = [];

    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
        ReactiveCommand.CreateFromTask(FetchLikedTracks);
        RefreshTracksCommand = ReactiveCommand.Create<bool>(ForceRefreshTracks);

        if (File.Exists(SpotifyAuth.CredentialsPath)) Task.Run(AuthenticateSpotify);

        // Create cache directory if it doesn't exist
        if (!Directory.Exists(_cacheFolder)) Directory.CreateDirectory(_cacheFolder);

        // Create JSON serializer options once
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    // New property for cache status
    private bool IsCacheEnabled { get; set; } = true;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            this.RaisePropertyChanged(nameof(FilteredTracks));
        }
    }

    // A computed property that filters the tracks based on search query
    public IEnumerable<TrackItemViewModel> FilteredTracks
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                // Reset all indices to their original positions
                for (var i = 0; i < _trackItems.Count; i++) _trackItems[i].DisplayIndex = i + 1;
                return _trackItems;
            }

            var filtered = _trackItems.Where(item =>
                item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Artists.Any(artist => artist.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                item.Album.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Update display indices for filtered results
            for (var i = 0; i < filtered.Count; i++) filtered[i].DisplayIndex = i + 1;

            return filtered;
        }
    }

    public FullPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);

            if (value == null) return;
            // Cancel any ongoing track loading operations
            CancelOngoingOperations();

            Task.Run(async () =>
            {
                try
                {
                    if (value.Id == "liked_songs_virtual")
                        await FetchLikedTracks();
                    else
                        await FetchPlaylistTracks(value);

                    // Ensure UI updates happen after task completion
                    RxApp.MainThreadScheduler.Schedule(() =>
                        this.RaisePropertyChanged(nameof(FilteredTracks)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching tracks: {ex.Message}");
                }
            });
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => this.RaiseAndSetIfChanged(ref _isAuthenticated, value);
    }

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        private set => this.RaiseAndSetIfChanged(ref _isAuthenticating, value);
    }

    public bool IsLoadingPlaylists
    {
        get => _isLoadingPlaylists;
        private set => this.RaiseAndSetIfChanged(ref _isLoadingPlaylists, value);
    }

    public bool IsLoadingTracks
    {
        get => _isLoadingTracks;
        private set => this.RaiseAndSetIfChanged(ref _isLoadingTracks, value);
    }

    public int LoadingProgress
    {
        get => _loadingProgress;
        private set => this.RaiseAndSetIfChanged(ref _loadingProgress, value);
    }

    public string LoadingStatusMessage
    {
        get => _loadingStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _loadingStatusMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ObservableCollection<FullPlaylist> Playlists
    {
        get => _playlists;
        private set => this.RaiseAndSetIfChanged(ref _playlists, value);
    }

    private ObservableCollection<FullTrack> Tracks
    {
        get => _tracks;
        set
        {
            this.RaiseAndSetIfChanged(ref _tracks, value);
            UpdateTrackItems();
        }
    }


    public ICommand AuthenticateCommand { get; }
    public ICommand RefreshTracksCommand { get; }

    // Implement IDisposable
    public void Dispose()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void UpdateTrackItems()
    {
        _trackItems.Clear();
        for (var i = 0; i < Tracks.Count; i++) _trackItems.Add(new TrackItemViewModel(Tracks[i], i + 1));
        this.RaisePropertyChanged(nameof(FilteredTracks));
    }

    private async Task AuthenticateSpotify()
    {
        try
        {
            IsAuthenticating = true;
            StatusMessage = "Authenticating with Spotify...";

            _spotifyClient = await SpotifyAuth.Authenticate();

            var user = await _spotifyClient.UserProfile.Current();
            StatusMessage = $"Authenticated as {user.DisplayName}";
            IsAuthenticated = true;

            // Fetch playlists after successful authentication
            await FetchUserPlaylists();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Authentication failed: {ex.Message}";
            IsAuthenticated = false;
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private async Task FetchUserPlaylists()
    {
        if (_spotifyClient == null) return;

        var user = await _spotifyClient.UserProfile.Current();

        try
        {
            IsLoadingPlaylists = true;
            StatusMessage = "Loading your playlists...";

            // Create the liked songs virtual playlist and add it first
            var likedSongsPlaylist = new FullPlaylist
            {
                Id = "liked_songs_virtual",
                Name = "Liked Songs",
                Description = "Songs you've liked on Spotify",
                Images =
                [
                    new Image
                    {
                        Url = "https://t.scdn.co/images/3099b3803ad9496896c43f22fe9be8c4.png"
                    }
                ],
                // Set owner to current user
                Owner = new PublicUser { Id = user.Id }
            };

            var allPlaylists = new ObservableCollection<FullPlaylist>
            {
                likedSongsPlaylist
            };

            var playlistsResponse = await _spotifyClient.Playlists.CurrentUsers();

            // Add initial batch of playlists
            foreach (var playlist in playlistsResponse.Items!)
                if (playlist.Owner?.Id == user.Id)
                    allPlaylists.Add(playlist);

            // Handle pagination to get all playlists
            while (playlistsResponse.Next != null)
            {
                playlistsResponse = await _spotifyClient.NextPage(playlistsResponse);
                foreach (var playlist in playlistsResponse.Items!) allPlaylists.Add(playlist);
            }

            Playlists = allPlaylists;
            StatusMessage = $"Loaded {allPlaylists.Count} playlists";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load playlists: {ex.Message}";
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }

    private async Task FetchPlaylistTracks(FullPlaylist playlist)
    {
        if (_spotifyClient == null) return;

        var playlistId = playlist.Id;
        if (playlistId == null) return;

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        var cachedTracks = await TryLoadTracksFromCacheAsync(playlistId, cancellationToken);
        if (IsCacheEnabled && cachedTracks.Count > 0)
        {
            Tracks = cachedTracks;
            StatusMessage = $"Loaded {cachedTracks.Count} tracks from cache";
            return;
        }

        try
        {
            Tracks.Clear();

            var user = await _spotifyClient.UserProfile.Current(cancellationToken);

            IsLoadingTracks = true;
            LoadingProgress = 0;
            StatusMessage = $"Loading tracks for playlist {playlist.Name}...";

            var totalTracks = playlist.Tracks?.Total ?? 0;

            cancellationToken.ThrowIfCancellationRequested();

            var tracksRequest = new PlaylistGetItemsRequest
            {
                Limit = 50,
                Market = user.Country
            };
            var tracksResponse = await _spotifyClient.Playlists.GetItems(playlistId, tracksRequest, cancellationToken);
            var allTracks = new ObservableCollection<FullTrack>();

            LoadingProgress = (int)(tracksResponse.Items!.Count * 100.0 / totalTracks);
            LoadingStatusMessage = $"Loaded {tracksResponse.Items.Count} of {totalTracks} tracks...";

            foreach (var track in tracksResponse.Items!)
                if (track.Track is FullTrack fullTrack)
                    allTracks.Add(fullTrack);

            while (tracksResponse.Next != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                tracksResponse = await _spotifyClient.NextPage(tracksResponse);

                cancellationToken.ThrowIfCancellationRequested();

                foreach (var track in tracksResponse.Items!)
                    if (track.Track is FullTrack fullTrack)
                        allTracks.Add(fullTrack);

                LoadingProgress = (int)(allTracks.Count * 100.0 / totalTracks);
                LoadingStatusMessage = $"Loaded {allTracks.Count} of {totalTracks} tracks...";
            }

            cancellationToken.ThrowIfCancellationRequested();

            Tracks = [.. allTracks];
            StatusMessage = $"Loaded {allTracks.Count} tracks";

            this.RaisePropertyChanged(nameof(FilteredTracks));
            if (IsCacheEnabled) await SaveTracksToCacheAsync(playlistId, allTracks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, no need to update UI
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                StatusMessage = $"Failed to load tracks for playlist {playlist.Name}: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsLoadingTracks = false;
        }
    }

    private async Task FetchLikedTracks()
    {
        if (_spotifyClient == null) return;

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        var cachedTracks = await TryLoadLikedTracksFromCacheAsync(cancellationToken);
        if (IsCacheEnabled && cachedTracks.Count > 0)
        {
            Tracks = cachedTracks;
            StatusMessage = $"Loaded {cachedTracks.Count} liked songs from cache";
            return;
        }

        try
        {
            Tracks.Clear();

            var user = await _spotifyClient.UserProfile.Current(cancellationToken);

            IsLoadingTracks = true;
            LoadingProgress = 0;
            StatusMessage = "Loading your liked songs...";
            LoadingStatusMessage = "Fetching your liked songs...";

            cancellationToken.ThrowIfCancellationRequested();

            var initialRequest = new LibraryTracksRequest { Limit = 50, Market = user.Country };
            var initialResponse = await _spotifyClient.Library.GetTracks(initialRequest, cancellationToken);
            var totalTracks = initialResponse.Total ?? 0;

            if (totalTracks == 0)
            {
                Tracks = [];
                StatusMessage = "No liked songs found";
                return;
            }

            const int maxConcurrentRequests = 25;

            var allTracks = new List<FullTrack>();
            var progressLock = new object();

            foreach (var item in initialResponse.Items!)
                if (item.Track is { } track)
                    allTracks.Add(track);

            var loadedCount = initialResponse.Items!.Count;
            LoadingProgress = (int)(loadedCount * 100.0 / totalTracks);
            LoadingStatusMessage = $"Loaded {loadedCount} of {totalTracks} tracks...";

            var tasks = new List<Task>();

            for (var offset = 50; offset < totalTracks; offset += 50)
            {
                if (tasks.Count >= maxConcurrentRequests)
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

                        var response = await _spotifyClient.Library.GetTracks(request, cancellationToken);

                        var batchTracks = new List<FullTrack>();
                        foreach (var item in response.Items!)
                            if (item.Track is { } track)
                                batchTracks.Add(track);

                        lock (progressLock)
                        {
                            allTracks.AddRange(batchTracks);
                            loadedCount += batchTracks.Count;
                            LoadingProgress = (int)(loadedCount * 100.0 / totalTracks);
                            LoadingStatusMessage = $"Loaded {loadedCount} of {totalTracks} tracks...";
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

            cancellationToken.ThrowIfCancellationRequested();

            Tracks = [.. allTracks];
            StatusMessage = $"Loaded {allTracks.Count} liked songs";

            this.RaisePropertyChanged(nameof(FilteredTracks));

            if (IsCacheEnabled) await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, no need to update UI
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested) StatusMessage = $"Failed to load liked songs: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsLoadingTracks = false;
        }
    }


    // Cache helper methods
    private async Task<ObservableCollection<FullTrack>> TryLoadTracksFromCacheAsync(string playlistId,
        CancellationToken cancellationToken)
    {
        var tracks = new ObservableCollection<FullTrack>();
        var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");

        if (!File.Exists(cacheFile)) return tracks;

        var fileInfo = new FileInfo(cacheFile);
        var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
        if (cacheAge > _cacheTtl) return tracks;

        try
        {
            // Check if file is empty
            if (fileInfo.Length == 0)
            {
                File.Delete(cacheFile);
                return tracks;
            }

            var json = await File.ReadAllTextAsync(cacheFile, cancellationToken);

            // Very simple validation check
            if (!json.StartsWith('[') || !json.EndsWith(']'))
            {
                Console.WriteLine($"Invalid JSON format in cache file: {cacheFile}");
                File.Delete(cacheFile);
                return tracks;
            }

            var cachedTracks = JsonSerializer.Deserialize<List<FullTrack>>(json, _jsonOptions);
            if (cachedTracks is { Count: > 0 })
                foreach (var track in cachedTracks.OfType<FullTrack>())
                {
                    // Ensure required properties are initialized
                    track.IsPlayable = true;
                    tracks.Add(track);
                }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            // Delete corrupted cache file
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // Ignore deletion errors
            }
        }

        return tracks;
    }

    private async Task<ObservableCollection<FullTrack>> TryLoadLikedTracksFromCacheAsync(
        CancellationToken cancellationToken)
    {
        var tracks = new ObservableCollection<FullTrack>();
        var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");

        if (!File.Exists(cacheFile)) return tracks;

        var fileInfo = new FileInfo(cacheFile);
        var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
        if (cacheAge > _cacheTtl) return tracks;

        try
        {
            // Check if file is empty
            if (fileInfo.Length == 0)
            {
                File.Delete(cacheFile);
                return tracks;
            }

            var json = await File.ReadAllTextAsync(cacheFile, cancellationToken);

            // Very simple validation check
            if (!json.StartsWith('[') || !json.EndsWith(']'))
            {
                Console.WriteLine($"Invalid JSON format in cache file: {cacheFile}");
                File.Delete(cacheFile);
                return tracks;
            }

            var cachedTracks = JsonSerializer.Deserialize<List<FullTrack>>(json, _jsonOptions);
            if (cachedTracks is { Count: > 0 })
                foreach (var track in cachedTracks.OfType<FullTrack>())
                {
                    // Ensure required properties are initialized
                    track.IsPlayable = true;
                    tracks.Add(track);
                }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            // Delete corrupted cache file
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // Ignore deletion errors
            }
        }

        return tracks;
    }

    private async Task SaveTracksToCacheAsync(string playlistId, ObservableCollection<FullTrack> tracks,
        CancellationToken cancellationToken)
    {
        try
        {
            var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");
            var json = JsonSerializer.Serialize(tracks.ToList(), _jsonOptions);
            await File.WriteAllTextAsync(cacheFile, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    private async Task SaveLikedTracksToCacheAsync(ObservableCollection<FullTrack> tracks,
        CancellationToken cancellationToken)
    {
        try
        {
            var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");
            var json = JsonSerializer.Serialize(tracks.ToList(), _jsonOptions);
            await File.WriteAllTextAsync(cacheFile, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    // Add a command to force refresh (bypass cache)
    private void ForceRefreshTracks(bool refreshAll = false)
    {
        SearchQuery = "";
        if (refreshAll)
            // Clear all cache files
            try
            {
                foreach (var file in Directory.GetFiles(_cacheFolder, "*.json")) File.Delete(file);
                StatusMessage = "Cache cleared. Refreshing data...";
            }
            catch
            {
                StatusMessage = "Failed to clear cache.";
            }

        // Reload the current playlist or liked songs without using cache
        var temp = IsCacheEnabled;
        IsCacheEnabled = false;

        if (SelectedPlaylist != null)
            Task.Run(async () =>
            {
                if (SelectedPlaylist.Id == "liked_songs_virtual")
                    await FetchLikedTracks();
                else
                    await FetchPlaylistTracks(SelectedPlaylist);

                // Restore cache setting after refresh
                IsCacheEnabled = temp;
            });
    }

    // Helper method to cancel ongoing operations
    private void CancelOngoingOperations()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        _currentLoadingCts = new CancellationTokenSource();
    }
}