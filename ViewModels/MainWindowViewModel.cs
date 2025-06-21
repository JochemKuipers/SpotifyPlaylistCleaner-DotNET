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
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;
using Image = SpotifyAPI.Web.Image;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int ProgressThreshold = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyPlaylistCleaner");

    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24);
    private readonly ObservableCollection<TrackItemViewModel> _trackItems = [];

    private CancellationTokenSource? _currentLoadingCts;

    private ObservableCollection<Duplicates.DuplicateGroup> _duplicateGroups;

    private HierarchicalTreeDataGridSource<ITreeNode>? _duplicateGroupsSource;

    private IEnumerable<TrackItemViewModel>? _filteredTracks;
    private IEnumerable<FullPlaylist>? _filteredPlaylists;
    private bool _isAuthenticated;
    private bool _isAuthenticating;

    private bool _isDuplicateViewVisible;
    private bool _isLoadingPlaylists;
    private bool _isLoadingTracks;
    private int _lastReportedProgress;
    private int _loadingProgress;
    private string _loadingStatusMessage = "";
    private ObservableCollection<FullPlaylist> _playlists = [];

    private string _searchQuery = "";
    private string _playlistSearchQuery = "";


    private bool _searchQueryChanged;
    private bool _playlistSearchQueryChanged;
    private FullPlaylist? _selectedPlaylist;
    private SpotifyClient? _spotifyClient;
    private string _statusMessage = "";
    private ObservableCollection<FullTrack> _tracks = [];


    public MainWindowViewModel()
    {
        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateSpotify);
        ReactiveCommand.CreateFromTask(FetchLikedTracks);
        RefreshTracksCommand = ReactiveCommand.Create<bool>(ForceRefreshTracks);
        ResetFiltersCommand = ReactiveCommand.Create(ResetFilters);
        FindDuplicatesCommand = ReactiveCommand.CreateFromTask(FindDuplicatesAsync);
        RemoveAllDuplicatesCommand = ReactiveCommand.Create(RemoveAllDuplicates);
        _duplicateGroups = [];
        IsDuplicateViewVisible = false;
        DeleteDuplicateCommand = new DelegateCommand<object>(DeleteDuplicateNode);

        BackToTracksCommand = ReactiveCommand.Create(() =>
        {
            IsDuplicateViewVisible = false;
            this.RaisePropertyChanged(nameof(IsTracksViewVisible));
        });


        if (File.Exists(SpotifyAuth.CredentialsPath)) Task.Run(AuthenticateSpotify);

        if (!Directory.Exists(_cacheFolder)) Directory.CreateDirectory(_cacheFolder);
    }

    private bool IsCacheEnabled { get; set; } = true;

    public bool IsDuplicateViewVisible
    {
        get => _isDuplicateViewVisible;
        set => this.RaiseAndSetIfChanged(ref _isDuplicateViewVisible, value);
    }

    public bool IsTracksViewVisible => !IsDuplicateViewVisible;

    public ICommand BackToTracksCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            _searchQueryChanged = true;
            this.RaisePropertyChanged(nameof(FilteredTracks));
        }
    }

    public string PlaylistSearchQuery
    {
        get => _playlistSearchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _playlistSearchQuery, value);
            _playlistSearchQueryChanged = true;
            this.RaisePropertyChanged(nameof(FilteredPlaylists));
        }
    }

    public IEnumerable<TrackItemViewModel> FilteredTracks
    {
        get
        {
            if (_filteredTracks != null && !_searchQueryChanged) return _filteredTracks;
            _searchQueryChanged = false;

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                _filteredTracks = _trackItems;
            }
            else
            {
                var search = SearchQuery.Trim().ToLower();
                _filteredTracks = _trackItems.Where(t =>
                    t.Name != null && (t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                       t.Artists.Any(a =>
                                           a.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))));
            }

            return _filteredTracks;
        }
    }

    public IEnumerable<FullPlaylist> FilteredPlaylists
    {
        get
        {
            if (_filteredPlaylists != null && !_playlistSearchQueryChanged) return _filteredPlaylists;
            _playlistSearchQueryChanged = false;

            if (string.IsNullOrWhiteSpace(PlaylistSearchQuery))
            {
                _filteredPlaylists = Playlists;
            }
            else
            {
                var search = PlaylistSearchQuery.Trim().ToLower();
                _filteredPlaylists = Playlists.Where(p =>
                    p.Name != null && p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
            }

            return _filteredPlaylists;
        }
    }

    public FullPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);

            if (value == null) return;
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

    private ObservableCollection<FullPlaylist> Playlists
    {
        get => _playlists;
        set
        {
            this.RaiseAndSetIfChanged(ref _playlists, value);
            this.RaisePropertyChanged(nameof(FilteredPlaylists));
        }

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

    public ObservableCollection<Duplicates.DuplicateGroup> DuplicateGroups
    {
        get => _duplicateGroups;
        set => this.RaiseAndSetIfChanged(ref _duplicateGroups, value);
    }

    public HierarchicalTreeDataGridSource<ITreeNode>? DuplicateGroupsSource
    {
        get => _duplicateGroupsSource;
        private set => this.RaiseAndSetIfChanged(ref _duplicateGroupsSource, value);
    }

    public ICommand AuthenticateCommand { get; }
    public ICommand RefreshTracksCommand { get; }
    public ICommand ResetFiltersCommand { get; }

    public ICommand FindDuplicatesCommand { get; }
    public ICommand RemoveAllDuplicatesCommand { get; }

    private ICommand DeleteDuplicateCommand { get; }


    public void Dispose()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ResetFilters()
    {
        SearchQuery = string.Empty;
    }

    private void UpdateTrackItems()
    {
        if (_trackItems.Count != Tracks.Count || _trackItems.Count == 0)
        {
            _trackItems.Clear();
            for (var i = 0; i < Tracks.Count; i++)
                _trackItems.Add(new TrackItemViewModel(Tracks[i], i + 1, DeleteTrack));
        }
        else
        {
            for (var i = 0; i < _trackItems.Count; i++)
                _trackItems[i].DisplayIndex = i + 1;
        }

        _filteredTracks = null;
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

            await FetchUserPlaylists();
            this.RaisePropertyChanged(nameof(FilteredPlaylists));
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

    private void DeleteTrack(FullTrack track)
    {
        if (_spotifyClient == null || SelectedPlaylist == null)
            return;

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;

        // First update UI collections
        var trackItem = _trackItems.FirstOrDefault(t => t.Track.Id == track.Id);
        if (trackItem != null)
            _trackItems.Remove(trackItem);

        var trackToRemove = Tracks.FirstOrDefault(t => t.Id == track.Id);
        if (trackToRemove != null)
            Tracks.Remove(trackToRemove);

        _filteredTracks = null;
        this.RaisePropertyChanged(nameof(FilteredTracks));

        // Then handle API deletion in background
        if (SelectedPlaylist.Id == "liked_songs_virtual")
        {
            Task.Run(async () =>
            {
                try
                {
                    await _spotifyClient.Library.RemoveTracks(
                        new LibraryRemoveTracksRequest([track.Id]),
                        cancellationToken);

                    if (IsCacheEnabled)
                        await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Removed '{track.Name}' from your liked songs";
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Failed to remove track: {ex.Message}";
                    });
                    Console.WriteLine($"API error removing track: {ex.Message}");
                }
            }, cancellationToken);
        }
        else if (SelectedPlaylist.Id != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _spotifyClient.Playlists.RemoveItems(
                        SelectedPlaylist.Id,
                        new PlaylistRemoveItemsRequest
                        {
                            Tracks = [new PlaylistRemoveItemsRequest.Item { Uri = track.Uri }]
                        },
                        cancellationToken);

                    if (IsCacheEnabled)
                        await SaveTracksToCacheAsync(SelectedPlaylist.Id, Tracks, cancellationToken);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Removed '{track.Name}' from playlist";
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Failed to remove track: {ex.Message}";
                    });
                    Console.WriteLine($"API error removing track: {ex.Message}");
                }
            }, cancellationToken);
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
                Owner = new PublicUser { Id = user.Id }
            };

            var tempPlaylists = new List<FullPlaylist> { likedSongsPlaylist };

            var playlistsResponse = await _spotifyClient.Playlists.CurrentUsers();

            foreach (var playlist in playlistsResponse.Items!.Where(playlist => playlist.Owner?.Id == user.Id))
                tempPlaylists.Add(playlist);
            while (playlistsResponse.Next != null)
            {
                playlistsResponse = await _spotifyClient.NextPage(playlistsResponse);
                foreach (var playlist in playlistsResponse.Items!) tempPlaylists.Add(playlist);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Playlists = new ObservableCollection<FullPlaylist>(tempPlaylists);
                StatusMessage = $"Loaded {tempPlaylists.Count} playlists";

                _filteredPlaylists = null;
                _playlistSearchQueryChanged = true;
                this.RaisePropertyChanged(nameof(FilteredPlaylists));
            });
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
        if (_spotifyClient == null || playlist.Id == null) return;

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;
        _lastReportedProgress = 0;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Tracks.Clear();
                IsLoadingTracks = true;
                LoadingProgress = 0;
                StatusMessage = $"Loading tracks for playlist '{playlist.Name}'...";
                LoadingStatusMessage = "Fetching playlist tracks...";
            });

            var cachedTracks = await TryLoadTracksFromCacheAsync(playlist.Id, cancellationToken);
            if (IsCacheEnabled && cachedTracks.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Tracks = new ObservableCollection<FullTrack>(cachedTracks);
                    StatusMessage = $"Loaded {cachedTracks.Count} tracks from cache";
                    IsLoadingTracks = false;
                });
                return;
            }

            var user = await _spotifyClient.UserProfile.Current(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var initialRequest = new PlaylistGetItemsRequest
            {
                Limit = 100,
                Market = user.Country
            };
            var initialResponse =
                await _spotifyClient.Playlists.GetItems(playlist.Id, initialRequest, cancellationToken);
            var totalTracks = initialResponse.Total ?? 0;

            if (totalTracks == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Tracks = [];
                    StatusMessage = "No tracks found in the playlist";
                    IsLoadingTracks = false;
                });
                return;
            }

            const int maxConcurrentRequests = 25;

            var allTracks = new List<FullTrack>();
            var progressLock = new object();

            foreach (var item in initialResponse.Items!)
                if (item.Track is FullTrack track)
                    allTracks.Add(track);

            var loadedCount = initialResponse.Items!.Count;

            var currentProgress = (int)(loadedCount * 100.0 / totalTracks);
            lock (progressLock)
            {
                _lastReportedProgress = currentProgress;
            }

            LoadingProgress = currentProgress;
            LoadingStatusMessage = $"Loaded {loadedCount} of {totalTracks} tracks...";

            var tasks = new List<Task>();

            for (var offset = 100; offset < totalTracks; offset += 100)
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

                        var request = new PlaylistGetItemsRequest
                        {
                            Market = user.Country,
                            Limit = 100,
                            Offset = currentOffset
                        };

                        var response = await _spotifyClient.Playlists.GetItems(playlist.Id, request, cancellationToken);

                        var batchTracks = new List<FullTrack>();
                        foreach (var item in response.Items!)
                            if (item.Track is FullTrack track)
                                batchTracks.Add(track);

                        lock (progressLock)
                        {
                            allTracks.AddRange(batchTracks);
                            loadedCount += batchTracks.Count;

                            var currentBatchProgress = (int)(loadedCount * 100.0 / totalTracks);
                            if (currentBatchProgress - _lastReportedProgress >= ProgressThreshold ||
                                currentBatchProgress == 100)
                            {
                                _lastReportedProgress = currentBatchProgress;
                                LoadingProgress = currentBatchProgress;
                                LoadingStatusMessage = $"Loaded {loadedCount} of {totalTracks} tracks...";
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

            cancellationToken.ThrowIfCancellationRequested();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Tracks = new ObservableCollection<FullTrack>(allTracks);
                StatusMessage = $"Loaded {allTracks.Count} tracks";
                IsLoadingTracks = false;
                LoadingProgress = 100;
                this.RaisePropertyChanged(nameof(FilteredTracks));
            });

            if (IsCacheEnabled)
                await SaveTracksToCacheAsync(playlist.Id, Tracks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                StatusMessage = $"Failed to load tracks: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsLoadingTracks = false;
        }
    }

    private async Task FetchLikedTracks()
    {
        if (_spotifyClient == null) return;

        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;
        _lastReportedProgress = 0;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Tracks.Clear();
                IsLoadingTracks = true;
                LoadingProgress = 0;
                StatusMessage = "Loading your liked songs...";
                LoadingStatusMessage = "Fetching your liked songs...";
            });

            var cachedTracks = await TryLoadLikedTracksFromCacheAsync(cancellationToken);
            if (IsCacheEnabled && cachedTracks.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Tracks = new ObservableCollection<FullTrack>(cachedTracks);
                    StatusMessage = $"Loaded {cachedTracks.Count} liked songs from cache";
                    IsLoadingTracks = false;
                });
                return;
            }

            var user = await _spotifyClient.UserProfile.Current(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var initialRequest = new LibraryTracksRequest { Limit = 50, Market = user.Country };
            var initialResponse = await _spotifyClient.Library.GetTracks(initialRequest, cancellationToken);
            var totalTracks = initialResponse.Total ?? 0;

            if (totalTracks == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Tracks = [];
                    StatusMessage = "No liked songs found";
                    IsLoadingTracks = false;
                });
                return;
            }

            const int maxConcurrentRequests = 25;

            var allTracks = new List<FullTrack>();
            var progressLock = new object();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in initialResponse.Items!)
                    if (item.Track is { } track)
                        allTracks.Add(track);
            });


            var loadedCount = initialResponse.Items!.Count;

            var currentProgress = (int)(loadedCount * 100.0 / totalTracks);
            lock (progressLock)
            {
                _lastReportedProgress = currentProgress;
            }

            LoadingProgress = currentProgress;
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
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var item in response.Items!)
                                if (item.Track is { } track)
                                    batchTracks.Add(track);
                        });

                        lock (progressLock)
                        {
                            allTracks.AddRange(batchTracks);
                            loadedCount += batchTracks.Count;

                            var currentBatchProgress = (int)(loadedCount * 100.0 / totalTracks);
                            if (currentBatchProgress - _lastReportedProgress >= ProgressThreshold ||
                                currentBatchProgress == 100)
                            {
                                _lastReportedProgress = currentBatchProgress;
                                LoadingProgress = currentBatchProgress;
                                LoadingStatusMessage = $"Loaded {loadedCount} of {totalTracks} tracks...";
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

            cancellationToken.ThrowIfCancellationRequested();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Tracks = new ObservableCollection<FullTrack>(allTracks);
                StatusMessage = $"Loaded {allTracks.Count} liked songs";
                IsLoadingTracks = false;
                LoadingProgress = 100;
                this.RaisePropertyChanged(nameof(FilteredTracks));
            });

            if (IsCacheEnabled)
                await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                StatusMessage = $"Failed to load liked songs: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsLoadingTracks = false;
        }
    }

    private Task FindDuplicatesAsync()
    {
        if (SelectedPlaylist == null || !_trackItems.Any())
        {
            StatusMessage = "Please select a playlist first";
            return Task.CompletedTask;
        }

        try
        {
            StatusMessage = "Finding duplicates...";

            var duplicatesFinder = new Duplicates(
                [.. _trackItems]
            );

            var duplicates = duplicatesFinder.FindAllDuplicates();

            DuplicateGroups.Clear();
            foreach (var group in duplicates) DuplicateGroups.Add(group);


            if (DuplicateGroups.Count > 0)
            {
                InitializeDuplicatesTreeDataGrid();
                IsDuplicateViewVisible = true;
                this.RaisePropertyChanged(nameof(IsTracksViewVisible));
                StatusMessage = $"Found {DuplicateGroups.Count} duplicate groups";
            }
            else
            {
                StatusMessage = "No duplicates found in this playlist";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error finding duplicates: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private void UpdateTrackIndices()
    {
        // Re-number all remaining track items with sequential indices
        for (int i = 0; i < _trackItems.Count; i++)
        {
            _trackItems[i].DisplayIndex = i + 1;
        }

        // Force UI refresh
        this.RaisePropertyChanged(nameof(FilteredTracks));
    }

    private void RemoveAllDuplicates()
    {
        if (!DuplicateGroups.Any())
        {
            StatusMessage = "No duplicates to remove";
            return;
        }

        var deletedCount = 0;
        var tracksToDelete = new List<(FullTrack Track, string PlaylistId)>();
        var currentPlaylistId = SelectedPlaylist?.Id;

        // First collect all tracks to delete
        foreach (var group in DuplicateGroups)
        {
            if (group.Tracks.Count <= 1)
                continue;

            // Find if the group has any explicit tracks
            bool hasExplicitTracks = group.Tracks.Any(t => t.Track.Explicit);
            bool allTracksExplicit = group.Tracks.All(t => t.Track.Explicit);

            // Index of the track to keep (default to 0)
            int indexToKeep = 0;

            // If there's a mix of explicit and non-explicit tracks, prioritize keeping an explicit one
            if (hasExplicitTracks && !allTracksExplicit)
            {
                // Find the first explicit track
                indexToKeep = group.Tracks.FindIndex(t => t.Track.Explicit);
            }

            // Mark tracks to delete (except the one we're keeping)
            for (int i = 0; i < group.Tracks.Count; i++)
            {
                if (i != indexToKeep)
                {
                    tracksToDelete.Add((group.Tracks[i].Track, currentPlaylistId!));
                    deletedCount++;
                }
            }
        }

        if (tracksToDelete.Count == 0)
        {
            StatusMessage = "No tracks to delete after applying rules";
            return;
        }

        // First, update the UI collections
        foreach (var (Track, PlaylistId) in tracksToDelete)
        {
            var track = Track;

            // Remove from UI track items
            var trackItem = _trackItems.FirstOrDefault(t => t.Track.Id == track.Id);
            if (trackItem != null)
            {
                _trackItems.Remove(trackItem);
            }

            // Remove from tracks collection
            var trackToRemove = Tracks.FirstOrDefault(t => t.Id == track.Id);
            if (trackToRemove != null)
            {
                Tracks.Remove(trackToRemove);
            }
        }

        UpdateTrackIndices();

        // Reset filtered tracks
        _filteredTracks = null;
        this.RaisePropertyChanged(nameof(FilteredTracks));

        // Now perform API deletions in background
        Task.Run(async () =>
        {
            var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;
            int successCount = 0;

            foreach (var (Track, PlaylistId) in tracksToDelete)
            {
                try
                {
                    var track = Track;
                    var playlistId = PlaylistId;

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (_spotifyClient == null)
                        break;

                    if (playlistId == "liked_songs_virtual")
                    {
                        await _spotifyClient.Library.RemoveTracks(
                            new LibraryRemoveTracksRequest([track.Id]),
                            cancellationToken);
                    }
                    else if (playlistId != null)
                    {
                        await _spotifyClient.Playlists.RemoveItems(
                            playlistId,
                            new PlaylistRemoveItemsRequest
                            {
                                Tracks = [new PlaylistRemoveItemsRequest.Item { Uri = track.Uri }]
                            },
                            cancellationToken);
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error removing track {Track.Name}: {ex.Message}");
                    // Continue with other tracks even if one fails
                }
            }

            // Update cache with remaining tracks
            try
            {
                if (IsCacheEnabled && currentPlaylistId != null)
                {
                    if (currentPlaylistId == "liked_songs_virtual")
                        await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);
                    else
                        await SaveTracksToCacheAsync(currentPlaylistId, Tracks, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating cache: {ex.Message}");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Successfully removed {successCount} of {deletedCount} duplicate tracks using smart selection";
            });
        });

        // Clean up UI state
        DuplicateGroups.Clear();
        IsDuplicateViewVisible = false;
        this.RaisePropertyChanged(nameof(IsTracksViewVisible));
    }

    private void InitializeDuplicatesTreeDataGrid()
    {
        if (DuplicateGroups == null || !DuplicateGroups.Any())
            throw new InvalidOperationException(
                "DuplicateGroups must be populated before initializing the TreeDataGrid.");

        var treeNodes = DuplicateGroups.Cast<ITreeNode>().ToList();

        var source = new HierarchicalTreeDataGridSource<ITreeNode>(treeNodes);

        var deleteCmd = DeleteDuplicateCommand;

        var deleteTemplate = new FuncDataTemplate<ITreeNode>(
            (node, _) => new Button
            {
                Content = "Delete",
                Command = deleteCmd,
                CommandParameter = node,
                Padding = new Thickness(4, 2),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        );

        var albumCoverTemplate = new FuncDataTemplate<ITreeNode>((node, _) =>
            {
                var image = new Avalonia.Controls.Image { Width = 40, Height = 40, Stretch = Stretch.Uniform };
                if (node?.DisplayImage != null) ImageLoader.SetSource(image, node.DisplayImage);
                return new Border
                {
                    Width = 50,
                    Height = 50,
                    Margin = new Thickness(5),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = image
                };
            }
        );


        source.Columns.Add(new HierarchicalExpanderColumn<ITreeNode>(
            new TextColumn<ITreeNode, string>(
                "Track",
                node => node.DisplayName,
                new GridLength(1, GridUnitType.Star)
            ),
            node => (node as Duplicates.DuplicateGroup)?.Tracks
        ));

        source.Columns.Add(new TemplateColumn<ITreeNode>(
            "Image",
            albumCoverTemplate,
            width: new GridLength(70, GridUnitType.Pixel)
        ));

        source.Columns.Add(
            new TextColumn<ITreeNode, string>(
                "Artists",
                node => node.DisplayArtist,
                new GridLength(1, GridUnitType.Star)
            )
        );

        source.Columns.Add(new TextColumn<ITreeNode, string>(
            "Album",
            node => (node as TrackItemViewModel)!.Album.Name,
            new GridLength(1, GridUnitType.Star)));

        source.Columns.Add(new TextColumn<ITreeNode, string>(
            "Uri",
            node => (node as TrackItemViewModel)!.Uri,
            new GridLength(1, GridUnitType.Star)));

        source.Columns.Add(new TextColumn<ITreeNode, string>(
            "Duration",
            static node =>
                (node as TrackItemViewModel)!.Duration,
            new GridLength(100, GridUnitType.Pixel)));

        source.Columns.Add(new TextColumn<ITreeNode, int>(
            "Count",
            static node => (node as Duplicates.DuplicateGroup)!.DuplicateCount,
            new GridLength(60, GridUnitType.Pixel)));

        source.Columns.Add(
            new TemplateColumn<ITreeNode>(
                "Delete",
                deleteTemplate,
                width: new GridLength(80, GridUnitType.Pixel)
            )
        );

        DuplicateGroupsSource = source;
    }

    private void DeleteDuplicateNode(object node)
    {
        switch (node)
        {
            case TrackItemViewModel track:
                var parentGroup = DuplicateGroups.FirstOrDefault(g => g.Tracks.Contains(track));
                if (parentGroup != null)
                {
                    DeleteTrack(track.Track);

                    parentGroup.Tracks.Remove(track);

                    if (parentGroup.Tracks.Count <= 1)
                        DuplicateGroups.Remove(parentGroup);

                    if (DuplicateGroups.Count > 0)
                    {
                        try
                        {
                            InitializeDuplicatesTreeDataGrid();
                        }
                        catch (InvalidOperationException)
                        {
                            // If there are no more duplicates, go back to track view
                            DuplicateGroups.Clear();
                            IsDuplicateViewVisible = false;
                            this.RaisePropertyChanged(nameof(IsTracksViewVisible));
                            Dispatcher.UIThread.Post(() => ForceRefreshTracks(false), DispatcherPriority.Background);
                            return;
                        }
                    }
                    else
                    {
                        // If there are no more duplicates, go back to track view
                        IsDuplicateViewVisible = false;
                        this.RaisePropertyChanged(nameof(IsTracksViewVisible));
                        Dispatcher.UIThread.Post(() => ForceRefreshTracks(false), DispatcherPriority.Background);
                        return;
                    }

                    StatusMessage = $"Removed track: {track.Name}";
                }
                break;

            case Duplicates.DuplicateGroup group:
                var tracksToDelete = new List<TrackItemViewModel>();
                for (var i = 1; i < group.Tracks.Count; i++)
                    tracksToDelete.Add(group.Tracks[i]);

                foreach (var trackToDelete in tracksToDelete)
                    DeleteTrack(trackToDelete.Track);

                DuplicateGroups.Remove(group);

                if (DuplicateGroups.Count > 0)
                {
                    try
                    {
                        InitializeDuplicatesTreeDataGrid();
                    }
                    catch (InvalidOperationException)
                    {
                        // If there are no more duplicates, go back to track view
                        DuplicateGroups.Clear();
                        IsDuplicateViewVisible = false;
                        this.RaisePropertyChanged(nameof(IsTracksViewVisible));
                        Dispatcher.UIThread.Post(() => ForceRefreshTracks(false), DispatcherPriority.Background);
                        return;
                    }
                }
                else
                {
                    // If there are no more duplicates, go back to track view
                    IsDuplicateViewVisible = false;
                    this.RaisePropertyChanged(nameof(IsTracksViewVisible));
                    Dispatcher.UIThread.Post(() => ForceRefreshTracks(false), DispatcherPriority.Background);
                    return;
                }

                StatusMessage = "Removed duplicate group";
                break;
        }
    }

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
            await using var fs = File.OpenRead(cacheFile);
            var cachedTracks =
                await JsonSerializer.DeserializeAsync<List<FullTrack>>(fs, JsonOptions, cancellationToken);
            if (cachedTracks != null)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var track in cachedTracks)
                        tracks.Add(track);
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
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
            await using var fs = File.OpenRead(cacheFile);
            var cachedTracks =
                await JsonSerializer.DeserializeAsync<List<FullTrack>>(fs, JsonOptions, cancellationToken);
            if (cachedTracks != null)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var track in cachedTracks)
                        tracks.Add(track);
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
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
            await using var fs = File.Create(cacheFile);
            await JsonSerializer.SerializeAsync(fs, tracks.ToList(), JsonOptions, cancellationToken);
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
            await using var fs = File.Create(cacheFile);
            await JsonSerializer.SerializeAsync(fs, tracks.ToList(), JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    private void ForceRefreshTracks(bool refreshAll = false)
    {
        SearchQuery = "";

        if (refreshAll)
        {
            try
            {
                foreach (var file in Directory.GetFiles(_cacheFolder, "*.json"))
                    File.Delete(file);
                StatusMessage = "Cache cleared. Refreshing data...";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to clear cache.";
                Console.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }

        var temp = IsCacheEnabled;
        IsCacheEnabled = false;

        var currentPlaylist = SelectedPlaylist;
        if (currentPlaylist == null)
        {
            StatusMessage = "No playlist selected to refresh";
            IsCacheEnabled = temp;
            return;
        }

        CancelOngoingOperations();

        _tracks.Clear();
        _trackItems.Clear();
        _filteredTracks = null;
        this.RaisePropertyChanged(nameof(FilteredTracks));

        Task.Run(async () =>
        {
            try
            {
                if (currentPlaylist.Id == "liked_songs_virtual")
                    await FetchLikedTracks();
                else
                    await FetchPlaylistTracks(currentPlaylist);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Refreshed tracks for '{currentPlaylist.Name}'";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Error refreshing tracks: {ex.Message}";
                });
                Console.WriteLine($"Error in ForceRefreshTracks: {ex.Message}");
            }
            finally
            {
                IsCacheEnabled = temp;
            }
        });

        IsDuplicateViewVisible = false;
        this.RaisePropertyChanged(nameof(IsTracksViewVisible));
    }

    private void CancelOngoingOperations()
    {
        _currentLoadingCts?.Cancel();
        _currentLoadingCts?.Dispose();
        _currentLoadingCts = new CancellationTokenSource();
    }

    public interface ITreeNode
    {
        string? DisplayName { get; }
        string? DisplayArtist { get; }
        string? DisplayImage { get; }
    }
}