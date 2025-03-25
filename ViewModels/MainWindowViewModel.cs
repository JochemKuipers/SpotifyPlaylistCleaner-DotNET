using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using ReactiveUI;

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


    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
        LoadLikedTracksCommand = ReactiveCommand.CreateFromTask(FetchLikedTracks);

        if (System.IO.File.Exists(SpotifyAuth.CredentialsPath))
        {
            Task.Run(AuthenticateSpotify);
        }
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

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        try
        {
            Tracks.Clear();

            IsLoadingTracks = true;
            LoadingProgress = 0;
            StatusMessage = $"Loading tracks for playlist {playlist.Name}...";

            var playlistId = playlist.Id;
            if (playlistId == null) return;

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
                // Check for cancellation before each API call
                cancellationToken.ThrowIfCancellationRequested();

                tracksResponse = await _spotifyClient.NextPage(tracksResponse);
                foreach (var track in tracksResponse.Items!)
                {
                    if (track.Track is FullTrack fullTrack)
                    {
                        allTracks.Add(fullTrack);
                    }
                }

                LoadingProgress = (int)(allTracks.Count * 100.0 / totalTracks);
                LoadingStatusMessage = $"Loaded {allTracks.Count} of {totalTracks} tracks...";
            }

            // Final cancellation check before updating UI
            cancellationToken.ThrowIfCancellationRequested();

            Tracks = [.. allTracks];
            StatusMessage = $"Loaded {allTracks.Count} tracks";
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
                cancellationToken.ThrowIfCancellationRequested();

                savedTracksResponse = await _spotifyClient.NextPage(savedTracksResponse);

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
