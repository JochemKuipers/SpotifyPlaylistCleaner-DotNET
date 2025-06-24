using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using SpotifyPlaylistCleaner_DotNET.Services;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class TrackListViewModel : ViewModelBase, IDisposable
{
    private readonly ISpotifyService _spotifyService;
    private readonly ICacheService _cacheService;
    private CancellationTokenSource? _cancellationTokenSource;

    private ObservableCollection<TrackModel> _tracks = [];
    private ObservableCollection<TrackModel> _filteredTracks = [];
    private string _searchQuery = string.Empty;
    private PlaylistModel? _currentPlaylist;
    private bool _isLoadingTracks;
    private int _loadingProgress;
    private string _loadingStatusMessage = string.Empty;

    private ObservableCollection<TrackModel> Tracks
    {
        get => _tracks;
        set => this.RaiseAndSetIfChanged(ref _tracks, value);
    }

    public ObservableCollection<TrackModel> FilteredTracks
    {
        get => _filteredTracks;
        private set => this.RaiseAndSetIfChanged(ref _filteredTracks, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            FilterTracks();
        }
    }

    public PlaylistModel? CurrentPlaylist
    {
        get => _currentPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPlaylist, value);
            if (value != null)
                LoadTracksForPlaylist(value);
        }
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

    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? TracksLoaded;

    public TrackListViewModel(ISpotifyService spotifyService, ICacheService cacheService)
    {
        _spotifyService = spotifyService;
        _cacheService = cacheService;

        RefreshTracksCommand = ReactiveCommand.Create(() => RefreshTracks(false));
        ClearCacheCommand = ReactiveCommand.Create(() => RefreshTracks(true));
        ResetFiltersCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);
        DeleteTrackCommand = ReactiveCommand.Create<TrackModel>(DeleteTrack);
    }

    public ICommand RefreshTracksCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand DeleteTrackCommand { get; }

    private async void LoadTracksForPlaylist(PlaylistModel playlist)
    {
        try
        {
            if (string.IsNullOrEmpty(playlist.Id))
                return;

            CancelCurrentOperations();
            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            try
            {
                IsLoadingTracks = true;
                LoadingProgress = 0;
                LoadingStatusMessage = $"Loading tracks for '{playlist.Name}'...";
                OnStatusMessageChanged($"Loading tracks for '{playlist.Name}'...");

                // Try loading from cache first
                var cachedTracks = playlist.IsLikedSongs
                    ? await _cacheService.LoadLikedTracksFromCacheAsync(cancellationToken)
                    : await _cacheService.LoadTracksFromCacheAsync(playlist.Id, cancellationToken);

                if (cachedTracks is { Count: > 0 })
                {
                    await LoadTracksFromCache(cachedTracks);
                    return;
                }

                // If not in cache, load from API with progress reporting
                var progress = new Progress<(int Loaded, int Total)>(progress =>
                {
                    LoadingProgress = (int)(progress.Loaded * 100.0 / Math.Max(1, progress.Total));
                    LoadingStatusMessage = $"Loaded {progress.Loaded} of {progress.Total} tracks...";
                });

                var tracks = playlist.IsLikedSongs
                    ? await _spotifyService.GetLikedTracks(progress, cancellationToken)
                    : await _spotifyService.GetPlaylistTracks(playlist.Id, progress, cancellationToken);

                var trackModels = tracks
                    .Select((t, i) => TrackModel.FromFullTrack(t, i + 1))
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Tracks = new ObservableCollection<TrackModel>(trackModels);
                    FilterTracks();

                    var trackText = playlist.IsLikedSongs ? "liked songs" : "tracks";
                    OnStatusMessageChanged($"Loaded {tracks.Count} {trackText}");

                    LoadingProgress = 100;
                    IsLoadingTracks = false;

                    TracksLoaded?.Invoke(this, EventArgs.Empty);
                });

                // Save to cache
                if (playlist.IsLikedSongs)
                    await _cacheService.SaveLikedTracksToCacheAsync([.. tracks], cancellationToken);
                else
                    await _cacheService.SaveTracksToCacheAsync(playlist.Id, [.. tracks], cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Operation was canceled, do nothing
            }
            catch (Exception ex)
            {
                var trackText = playlist.IsLikedSongs ? "liked songs" : "tracks";
                OnStatusMessageChanged($"Failed to load {trackText}: {ex.Message}");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                    IsLoadingTracks = false;
            }
        }
        catch (Exception e)
        {
            OnStatusMessageChanged($"Error loading tracks: {e.Message}");
            IsLoadingTracks = false;
            LoadingProgress = 0;
            LoadingStatusMessage = string.Empty;
        }
    }

    private async Task LoadTracksFromCache(IList<FullTrack> cachedTracks)
    {
        var trackModels = cachedTracks
            .Select((t, i) => TrackModel.FromFullTrack(t, i + 1))
            .ToList();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Tracks = new ObservableCollection<TrackModel>(trackModels);
            FilterTracks();

            var trackText = CurrentPlaylist!.IsLikedSongs ? "liked songs" : "tracks";
            OnStatusMessageChanged($"Loaded {cachedTracks.Count} {trackText} from cache");

            IsLoadingTracks = false;
            TracksLoaded?.Invoke(this, EventArgs.Empty);
        });
    }

    private void FilterTracks()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredTracks = new ObservableCollection<TrackModel>(Tracks);
        }
        else
        {
            var search = SearchQuery.Trim().ToLower();
            var filtered = Tracks.Where(t =>
                (t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) ||
                t.Artists.Any(a => a.Contains(search, StringComparison.CurrentCultureIgnoreCase))).ToList();
            FilteredTracks = new ObservableCollection<TrackModel>(filtered);
        }
    }

    public void RefreshTracks(bool clearCache)
    {
        if (CurrentPlaylist == null)
        {
            OnStatusMessageChanged("No playlist selected to refresh");
            return;
        }

        if (clearCache)
        {
            _cacheService.ClearCache();
            OnStatusMessageChanged("Cache cleared. Refreshing data...");
        }

        var cacheSetting = _cacheService.IsCacheEnabled;
        _cacheService.IsCacheEnabled = false;

        CancelCurrentOperations();
        Tracks.Clear();
        FilteredTracks.Clear();

        // Reload tracks
        LoadTracksForPlaylist(CurrentPlaylist);

        // Restore cache setting
        _cacheService.IsCacheEnabled = cacheSetting;
    }

    private async void DeleteTrack(TrackModel track)
    {
        try
        {
            if (string.IsNullOrEmpty(track.Id) || CurrentPlaylist == null)
                return;

            // Remove from UI first
            Tracks.Remove(track);
            UpdateTrackIndices();
            FilterTracks();

            CancelCurrentOperations();
            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            // Then remove from Spotify
            try
            {
                if (CurrentPlaylist.IsLikedSongs)
                {
                    await _spotifyService.RemoveTrackFromLikedSongs(track.Id, cancellationToken);
                    OnStatusMessageChanged($"Removed '{track.Name}' from your liked songs");
                }
                else
                {
                    await _spotifyService.RemoveTrackFromPlaylist(CurrentPlaylist.Id, track.Uri, cancellationToken);
                    OnStatusMessageChanged($"Removed '{track.Name}' from playlist");
                }

                // Update cache
                if (!_cacheService.IsCacheEnabled) return;
                var fullTracks = Tracks.Select(t => new FullTrack
                {
                    Id = t.Id,
                    Uri = t.Uri,
                    Name = t.Name,
                    Artists = [.. t.Artists.Select(a => new SimpleArtist { Name = a })],
                    Album = new SimpleAlbum { Name = t.Album },
                    DurationMs = t.DurationMs,
                    IsPlayable = t.IsPlayable,
                    Explicit = t.IsExplicit,
                    IsLocal = t.IsLocal
                }).ToList();

                if (CurrentPlaylist.IsLikedSongs)
                    await _cacheService.SaveLikedTracksToCacheAsync(fullTracks, cancellationToken);
                else
                    await _cacheService.SaveTracksToCacheAsync(CurrentPlaylist.Id, fullTracks, cancellationToken);
            }
            catch (Exception ex)
            {
                OnStatusMessageChanged($"Failed to remove track: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            OnStatusMessageChanged($"Error deleting track: {e.Message}");
        }
        finally
        {
            // Ensure we always update indices after deletion
            UpdateTrackIndices();
            FilterTracks();
        }
    }

    private void UpdateTrackIndices()
    {
        for (var i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].DisplayIndex = i + 1;
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