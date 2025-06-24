using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyPlaylistCleaner_DotNET.Models;
using SpotifyPlaylistCleaner_DotNET.Services;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class PlaylistViewModel : ViewModelBase, IDisposable
{
    private readonly ISpotifyService _spotifyService;
    private CancellationTokenSource? _cancellationTokenSource;

    private ObservableCollection<PlaylistModel> _playlists = [];
    private ObservableCollection<PlaylistModel> _filteredPlaylists = [];
    private string _playlistSearchQuery = string.Empty;
    private PlaylistModel? _selectedPlaylist;
    private bool _isLoadingPlaylists;

    private ObservableCollection<PlaylistModel> Playlists
    {
        get => _playlists;
        set => this.RaiseAndSetIfChanged(ref _playlists, value);
    }

    public ObservableCollection<PlaylistModel> FilteredPlaylists
    {
        get => _filteredPlaylists;
        private set => this.RaiseAndSetIfChanged(ref _filteredPlaylists, value);
    }

    public string PlaylistSearchQuery
    {
        get => _playlistSearchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _playlistSearchQuery, value);
            FilterPlaylists();
        }
    }

    public PlaylistModel? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
            if (value != null)
                PlaylistSelected?.Invoke(this, value);
        }
    }

    public bool IsLoadingPlaylists
    {
        get => _isLoadingPlaylists;
        private set => this.RaiseAndSetIfChanged(ref _isLoadingPlaylists, value);
    }

    public event EventHandler<PlaylistModel>? PlaylistSelected;
    public event EventHandler<string>? StatusMessageChanged;

    public PlaylistViewModel(ISpotifyService spotifyService)
    {
        _spotifyService = spotifyService;

        RefreshPlaylistsCommand = ReactiveCommand.CreateFromTask(LoadPlaylistsAsync);
    }

    public ICommand RefreshPlaylistsCommand { get; }

    public async Task LoadPlaylistsAsync()
    {
        CancelCurrentOperations();
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            IsLoadingPlaylists = true;
            OnStatusMessageChanged("Loading your playlists...");

            var spotifyPlaylists = await _spotifyService.GetUserPlaylists(cancellationToken);

            var playlistModels = spotifyPlaylists
                .Select(PlaylistModel.FromFullPlaylist)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Playlists = new ObservableCollection<PlaylistModel>(playlistModels);
                FilterPlaylists();
                OnStatusMessageChanged($"Loaded {playlistModels.Count} playlists");
            });
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, do nothing
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Failed to load playlists: {ex.Message}");
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }

    private void FilterPlaylists()
    {
        if (string.IsNullOrWhiteSpace(PlaylistSearchQuery))
        {
            FilteredPlaylists = new ObservableCollection<PlaylistModel>(Playlists);
        }
        else
        {
            var search = PlaylistSearchQuery.Trim().ToLower();
            var filtered = Playlists.Where(p =>
                p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
            FilteredPlaylists = new ObservableCollection<PlaylistModel>(filtered);
        }
    }

    private void CancelCurrentOperations()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    private void OnStatusMessageChanged(string message)
    {
        StatusMessageChanged?.Invoke(this, message);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}