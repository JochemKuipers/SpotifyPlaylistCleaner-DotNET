using System;
using System.Threading.Tasks;
using System.Windows.Input;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using ReactiveUI;
namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private string _greeting = "Welcome to Avalonia!";
    private bool _isAuthenticated;
    private bool _isAuthenticating;
    private SpotifyClient? _spotifyClient;
    private string _statusMessage = "";

    public string Greeting 
    {
        get => _greeting;
        set => this.RaiseAndSetIfChanged(ref _greeting, value);
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
    
    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }
    
    public ICommand AuthenticateCommand { get; }
    
    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
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
}