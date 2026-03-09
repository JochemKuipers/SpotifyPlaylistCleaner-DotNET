using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using SpotifyPlaylistCleaner_DotNET.Services;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IAuthenticationService _authService;

    private bool _isAuthenticated;
    private bool _isAuthenticating;
    private string _statusMessage = string.Empty;

    public PlaylistViewModel PlaylistViewModel { get; }
    public TrackListViewModel TrackListViewModel { get; }
    public DuplicatesViewModel DuplicatesViewModel { get; }

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public MainWindowViewModel(
        IAuthenticationService authService,
        ISpotifyService spotifyService,
        IDuplicatesService duplicatesService)
    {
        _authService = authService;

        // Create child view models
        PlaylistViewModel = new PlaylistViewModel(spotifyService);
        TrackListViewModel = new TrackListViewModel(spotifyService);
        DuplicatesViewModel = new DuplicatesViewModel(duplicatesService, spotifyService);

        // Set up event handlers
        PlaylistViewModel.PlaylistSelected += (sender, playlist) => TrackListViewModel.CurrentPlaylist = playlist;
        PlaylistViewModel.StatusMessageChanged += (sender, message) => StatusMessage = message;
        TrackListViewModel.StatusMessageChanged += (sender, message) => StatusMessage = message;
        DuplicatesViewModel.StatusMessageChanged += (sender, message) => StatusMessage = message;
        DuplicatesViewModel.BackToTracksView += (sender, args) => DuplicatesViewModel.IsDuplicatesViewVisible = false;
        DuplicatesViewModel.DuplicatesRemoved += (sender, args) => TrackListViewModel.RefreshTracks();

        // Create commands
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
        FindDuplicatesCommand = ReactiveCommand.Create(() =>
            DuplicatesViewModel.FindDuplicatesCommand.Execute(TrackListViewModel.FilteredTracks));

        // Try to authenticate if credentials exist
        Task.Run(AuthenticateSpotify);
    }

    public ICommand AuthenticateCommand { get; }
    public ICommand FindDuplicatesCommand { get; }

    private async Task AuthenticateSpotify()
    {
        try
        {
            IsAuthenticating = true;
            StatusMessage = "Authenticating with Spotify...";

            await _authService.Authenticate();
            var displayName = await _authService.GetUserDisplayName();

            StatusMessage = $"Authenticated as {displayName}";
            IsAuthenticated = true;

            // Load playlists after successful authentication
            await PlaylistViewModel.LoadPlaylistsAsync();
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

    public void Dispose()
    {
        PlaylistViewModel.Dispose();
        TrackListViewModel.Dispose();
        DuplicatesViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}