using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using ReactiveUI;
using System.Collections.Generic;
using System.Linq;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private bool _isAuthenticated;
    private bool _isAuthenticating;
    private bool _isLoadingPlaylists;
    private bool _isLoadingTracks;
    private int _loadingProgress;
    private string _loadingStatusMessage = "";
    private SpotifyClient? _spotifyClient;
    private string _statusMessage = "";
    private ObservableCollection<FullPlaylist> _playlists = [];
    private ObservableCollection<FullTrack> _tracks = [];
    private FullPlaylist? _selectedPlaylist;

    // Add a CancellationTokenSource for managing API requests
    private CancellationTokenSource? _currentLoadingCts;

    // Add these properties for cache management
    private readonly string _cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyPlaylistCleaner");
    private readonly TimeSpan _cacheTTL = TimeSpan.FromHours(24); // Cache valid for 24 hours
    private readonly Dictionary<string, DateTime> _playlistCacheTimes = [];

    // New property for cache status
    public bool IsCacheEnabled { get; set; } = true;

    // Add this field to your class
    private readonly JsonSerializerOptions _jsonOptions;

    public FullPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);

            if (value != null)
            {
                // Cancel any ongoing track loading operations
                CancelOngoingOperations();

                if (value.Id == "liked_songs_virtual")
                {
                    // User selected "Liked Songs"
                    Task.Run(FetchLikedTracks);
                }
                else
                {
                    // User selected a real playlist
                    Task.Run(() => FetchPlaylistTracks(value));
                }
            }
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

    public ObservableCollection<FullTrack> Tracks
    {
        get => _tracks;
        private set => this.RaiseAndSetIfChanged(ref _tracks, value);
    }

    public ICommand AuthenticateCommand { get; }
    public ICommand LoadLikedTracksCommand { get; }
    public ICommand RefreshTracksCommand { get; }

    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
        LoadLikedTracksCommand = ReactiveCommand.CreateFromTask(FetchLikedTracks);
        RefreshTracksCommand = ReactiveCommand.Create<bool>(ForceRefreshTracks);

        if (System.IO.File.Exists(SpotifyAuth.CredentialsPath))
        {
            Task.Run(AuthenticateSpotify);
        }

        // Create cache directory if it doesn't exist
        if (!Directory.Exists(_cacheFolder))
        {
            Directory.CreateDirectory(_cacheFolder);
        }

        // Create JSON serializer options once
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve
        };
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

        PrivateUser user = await _spotifyClient.UserProfile.Current();

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
                Images = [
                    new Image {
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
            {
                if (playlist.Owner?.Id == user.Id)
                {
                    allPlaylists.Add(playlist);
                }
            }

            // Handle pagination to get all playlists
            while (playlistsResponse.Next != null)
            {
                playlistsResponse = await _spotifyClient.NextPage(playlistsResponse);
                foreach (var playlist in playlistsResponse.Items!)
                {
                    allPlaylists.Add(playlist);
                }
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

        // Check if we have a valid cache for this playlist
        if (IsCacheEnabled && TryLoadTracksFromCache(playlistId, out var cachedTracks))
        {
            // Use cached data
            Tracks = cachedTracks;
            StatusMessage = $"Loaded {cachedTracks.Count} tracks from cache";
            return;
        }

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        try
        {
            Tracks.Clear();

            IsLoadingTracks = true;
            LoadingProgress = 0;
            StatusMessage = $"Loading tracks for playlist {playlist.Name}...";

            var totalTracks = playlist.Tracks?.Total ?? 0;

            // Check for cancellation before API call
            cancellationToken.ThrowIfCancellationRequested();

            var tracksResponse = await _spotifyClient.Playlists.GetItems(playlistId, cancellationToken);
            var allTracks = new ObservableCollection<FullTrack>();

            LoadingProgress = (int)(tracksResponse.Items!.Count * 100.0 / totalTracks);
            LoadingStatusMessage = $"Loaded {tracksResponse.Items.Count} of {totalTracks} tracks...";

            // Add initial batch of tracks
            foreach (var track in tracksResponse.Items!)
            {
                if (track.Track is FullTrack fullTrack)
                {
                    allTracks.Add(fullTrack);
                }
            }

            // Handle pagination to get all tracks
            while (tracksResponse.Next != null)
            {
                // Check cancellation BEFORE API call
                cancellationToken.ThrowIfCancellationRequested();

                // Make the API call (no token parameter)
                tracksResponse = await _spotifyClient.NextPage(tracksResponse);

                // Check cancellation AFTER API call but BEFORE processing
                cancellationToken.ThrowIfCancellationRequested();

                // Process results...
                foreach (var track in tracksResponse.Items!)
                {
                    if (track.Track is FullTrack fullTrack)
                    {
                        allTracks.Add(fullTrack);
                    }
                }

                // Update progress
                LoadingProgress = (int)(allTracks.Count * 100.0 / totalTracks);
                LoadingStatusMessage = $"Loaded {allTracks.Count} of {totalTracks} tracks...";
            }

            // Final cancellation check before updating UI
            cancellationToken.ThrowIfCancellationRequested();

            Tracks = [.. allTracks];
            StatusMessage = $"Loaded {allTracks.Count} tracks";

            // Save to cache
            if (IsCacheEnabled)
            {
                SaveTracksToCache(playlistId, allTracks);
            }
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, no need to update UI
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusMessage = $"Failed to load tracks for playlist {playlist.Name}: {ex.Message}";
            }
        }
        finally
        {
            // Only update if not canceled
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoadingTracks = false;
            }
        }
    }

    private async Task FetchLikedTracks()
    {
        if (_spotifyClient == null) return;

        // Check if we have a valid cache for liked songs
        if (IsCacheEnabled && TryLoadLikedTracksFromCache(out var cachedTracks))
        {
            // Use cached data
            Tracks = cachedTracks;
            StatusMessage = $"Loaded {cachedTracks.Count} liked songs from cache";
            return;
        }

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        try
        {
            Tracks.Clear();

            IsLoadingTracks = true;
            LoadingProgress = 0;
            StatusMessage = "Loading your liked songs...";
            LoadingStatusMessage = "Fetching your liked songs...";

            cancellationToken.ThrowIfCancellationRequested();

            // Get first page of saved tracks
            var savedTracksResponse = await _spotifyClient.Library.GetTracks(cancellationToken);
            var totalTracks = savedTracksResponse.Total ?? 0;
            var allTracks = new ObservableCollection<FullTrack>();

            // Initial progress update
            LoadingProgress = totalTracks > 0 ? (int)(savedTracksResponse.Items!.Count * 100.0 / totalTracks) : 0;
            LoadingStatusMessage = $"Loaded {savedTracksResponse.Items!.Count} of {totalTracks} tracks...";

            // Process first batch of tracks
            foreach (var item in savedTracksResponse.Items)
            {
                if (item.Track is FullTrack track)
                {
                    allTracks.Add(track);
                }
            }

            // Handle pagination to get all tracks
            while (savedTracksResponse.Next != null)
            {
                // Check cancellation BEFORE API call
                cancellationToken.ThrowIfCancellationRequested();

                // Make the API call (no token parameter)
                savedTracksResponse = await _spotifyClient.NextPage(savedTracksResponse);

                // Check cancellation AFTER API call but BEFORE processing
                cancellationToken.ThrowIfCancellationRequested();

                // Process results...
                foreach (var item in savedTracksResponse.Items!)
                {
                    if (item.Track is FullTrack track)
                    {
                        allTracks.Add(track);
                    }
                }

                // Update progress
                LoadingProgress = (int)(allTracks.Count * 100.0 / totalTracks);
                LoadingStatusMessage = $"Loaded {allTracks.Count} of {totalTracks} tracks...";
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Update UI with all tracks
            Tracks = [.. allTracks];
            StatusMessage = $"Loaded {allTracks.Count} liked songs";

            // Save to cache
            if (IsCacheEnabled)
            {
                SaveLikedTracksToCache(allTracks);
            }
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, no need to update UI
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusMessage = $"Failed to load liked songs: {ex.Message}";
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoadingTracks = false;
            }
        }
    }

    // Cache helper methods
    private bool TryLoadTracksFromCache(string playlistId, out ObservableCollection<FullTrack> tracks)
    {
        tracks = [];
        var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");

        // Check if cache exists and is not expired
        if (File.Exists(cacheFile))
        {
            var fileInfo = new FileInfo(cacheFile);
            var cacheAge = DateTime.Now - fileInfo.LastWriteTime;

            if (cacheAge <= _cacheTTL)
            {
                try
                {
                    var json = File.ReadAllText(cacheFile);
                    var cachedTracks = JsonSerializer.Deserialize<List<FullTrack>>(json, _jsonOptions);
                    if (cachedTracks != null)
                    {
                        tracks = [.. cachedTracks];
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cache loading error: {ex.Message}");
                    // Cache file might be corrupted, continue with API call
                }
            }
        }

        return false;
    }

    private bool TryLoadLikedTracksFromCache(out ObservableCollection<FullTrack> tracks)
    {
        tracks = [];
        var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");

        // Check if cache exists and is not expired
        if (File.Exists(cacheFile))
        {
            var fileInfo = new FileInfo(cacheFile);
            var cacheAge = DateTime.Now - fileInfo.LastWriteTime;

            if (cacheAge <= _cacheTTL)
            {
                try
                {
                    var json = File.ReadAllText(cacheFile);
                    var cachedTracks = JsonSerializer.Deserialize<List<FullTrack>>(json, _jsonOptions);
                    if (cachedTracks != null)
                    {
                        tracks = [.. cachedTracks];
                        return true;
                    }
                }
                catch
                {
                    // Cache file might be corrupted, continue with API call
                }
            }
        }

        return false;
    }

    private void SaveTracksToCache(string playlistId, ObservableCollection<FullTrack> tracks)
    {
        try
        {
            var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");
            var json = JsonSerializer.Serialize(tracks.ToList(), _jsonOptions);
            File.WriteAllText(cacheFile, json);
            _playlistCacheTimes[playlistId] = DateTime.Now;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
            // Non-critical, app can continue without caching
        }
    }

    private void SaveLikedTracksToCache(ObservableCollection<FullTrack> tracks)
    {
        try
        {
            var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");
            var json = JsonSerializer.Serialize(tracks.ToList(), _jsonOptions);
            File.WriteAllText(cacheFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    // Add a command to force refresh (bypass cache)
    private void ForceRefreshTracks(bool refreshAll = false)
    {
        if (refreshAll)
        {
            // Clear all cache files
            try
            {
                foreach (var file in Directory.GetFiles(_cacheFolder, "*.json"))
                {
                    File.Delete(file);
                }
                StatusMessage = "Cache cleared. Refreshing data...";
            }
            catch
            {
                StatusMessage = "Failed to clear cache.";
            }
        }

        // Reload the current playlist or liked songs without using cache
        var temp = IsCacheEnabled;
        IsCacheEnabled = false;

        if (SelectedPlaylist != null)
        {
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
    }

    // Helper method to cancel ongoing operations
    private void CancelOngoingOperations()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        _currentLoadingCts = new CancellationTokenSource();
    }

    // Implement IDisposable
    public void Dispose()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
