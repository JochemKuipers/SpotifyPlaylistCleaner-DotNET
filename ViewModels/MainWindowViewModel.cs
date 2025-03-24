using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using ReactiveUI;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private bool _isAuthenticated;
    private bool _isAuthenticating;
    private bool _isLoadingPlaylists;
    private SpotifyClient? _spotifyClient;
    private string _statusMessage = "";
    private ObservableCollection<FullPlaylist> _playlists = [];
    private ObservableCollection<FullTrack> _tracks = [];
    private FullPlaylist? _selectedPlaylist;

    public FullPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
            if (value != null)
            {
                Task.Run(() => FetchPlaylistTracks(value));
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
    
    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);

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
        
        try
        {
            IsLoadingPlaylists = true;
            StatusMessage = "Loading your playlists...";
            
            var playlistsResponse = await _spotifyClient.Playlists.CurrentUsers();
            var allPlaylists = new ObservableCollection<FullPlaylist>();
            
            // Add initial batch of playlists
            foreach (var playlist in playlistsResponse.Items!)
            {
                Console.WriteLine($"Playlist: {playlist.Name}, Images: {playlist.Images?.Count ?? 0}");
                if (playlist.Images?.Count > 0)
                {
                    Console.WriteLine($"First image URL: {playlist.Images[0].Url}");
                }
                allPlaylists.Add(playlist);
            }
            
            // Handle pagination to get all playlists
            while (playlistsResponse.Next != null)
            {
                playlistsResponse = await _spotifyClient.NextPage(playlistsResponse);
                foreach (var playlist in playlistsResponse.Items!)
                {
                    Console.WriteLine($"Playlist: {playlist.Name}, Images: {playlist.Images?.Count ?? 0}");
                    if (playlist.Images?.Count > 0)
                    {
                        Console.WriteLine($"First image URL: {playlist.Images[0].Url}");
                    }
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

    private async Task FetchPlaylistTracks(FullPlaylist playlist){
        if (_spotifyClient == null) return;
        
        try
        {
            IsLoadingPlaylists = true;
            StatusMessage = $"Loading tracks for playlist {playlist.Name}...";

            var playlistId = playlist.Id;
            if (playlistId == null) return;
            
            var tracksResponse = await _spotifyClient.Playlists.GetItems(playlistId);
            var allTracks = new ObservableCollection<FullTrack>();
            
            // Add initial batch of tracks
            foreach (var track in tracksResponse.Items!)
            {
                FullTrack fullTrack = (FullTrack)track.Track;
                Console.WriteLine($"Track: {fullTrack.Name}, Artists: {fullTrack.Artists.Count}");
                allTracks.Add(fullTrack);
            }
            
            // Handle pagination to get all tracks
            while (tracksResponse.Next != null)
            {
                tracksResponse = await _spotifyClient.NextPage(tracksResponse);
                foreach (var track in tracksResponse.Items!)
                {
                    FullTrack fullTrack = (FullTrack)track.Track;
                    Console.WriteLine($"Track: {fullTrack.Name}, Artists: {fullTrack.Artists.Count}");
                    allTracks.Add(fullTrack);
                }
            }

            Tracks = allTracks;
            StatusMessage = $"Loaded {allTracks.Count} tracks for playlist {playlist.Name}";

        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load tracks for playlist {playlist.Name}: {ex.Message}";
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }
}