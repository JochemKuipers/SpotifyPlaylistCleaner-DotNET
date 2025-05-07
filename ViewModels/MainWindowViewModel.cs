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

    // Add a command to go back to the regular tracks view

    private CancellationTokenSource? _currentLoadingCts;

    // Add this property

    private ObservableCollection<Duplicates.DuplicateGroup> _duplicateGroups;

    // Update the property type to use ITreeNode
    private HierarchicalTreeDataGridSource<ITreeNode>? _duplicateGroupsSource;

    private IEnumerable<TrackItemViewModel>? _filteredTracks;
    private bool _isAuthenticated;
    private bool _isAuthenticating;

    // Update the IsDuplicateViewVisible property to properly notify changes
    private bool _isDuplicateViewVisible;
    private bool _isLoadingPlaylists;
    private bool _isLoadingTracks;
    private int _lastReportedProgress;
    private int _loadingProgress;
    private string _loadingStatusMessage = "";
    private ObservableCollection<FullPlaylist> _playlists = [];

    private string _searchQuery = "";

    private bool _searchQueryChanged;
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

        // Add the new command
        BackToTracksCommand = ReactiveCommand.Create(() =>
        {
            IsDuplicateViewVisible = false;
            this.RaisePropertyChanged(nameof(IsTracksViewVisible));
        });


        if (File.Exists(SpotifyAuth.CredentialsPath)) Task.Run(AuthenticateSpotify);

        // Create a cache directory if it doesn't exist
        if (!Directory.Exists(_cacheFolder)) Directory.CreateDirectory(_cacheFolder);
    }

    private bool IsCacheEnabled { get; set; } = true;

    public bool IsDuplicateViewVisible
    {
        get => _isDuplicateViewVisible;
        set => this.RaiseAndSetIfChanged(ref _isDuplicateViewVisible, value);
    }

    // Add a property to control the visibility of the regular tracks view
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
        // Only update if the counts differ or this is the initial load
        if (_trackItems.Count != Tracks.Count || _trackItems.Count == 0)
        {
            _trackItems.Clear();
            for (var i = 0; i < Tracks.Count; i++)
                _trackItems.Add(new TrackItemViewModel(Tracks[i], i + 1, DeleteTrack));
        }
        else
        {
            // Update indices if we have the same number of tracks
            for (var i = 0; i < _trackItems.Count; i++)
                _trackItems[i].DisplayIndex = i + 1;
        }

        _filteredTracks = null; // Invalidate cached filtered tracks
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
        var cancellationToken = _currentLoadingCts?.Token ?? CancellationToken.None;
        if (_spotifyClient == null) return;

        // Find and remove the track item from the collection
        var trackItem = _trackItems.FirstOrDefault(t => t.Track == track);
        if (trackItem != null)
            _trackItems.Remove(trackItem);

        // Remove from the track collection
        Tracks.Remove(track);

        // Reset filtered tracks cache
        _filteredTracks = null;
        this.RaisePropertyChanged(nameof(FilteredTracks));

        // Update Spotify via API
        if (SelectedPlaylist == null) return;
        if (SelectedPlaylist.Id == "liked_songs_virtual")
            Task.Run(async () =>
            {
                try
                {
                    await _spotifyClient.Library.RemoveTracks(new LibraryRemoveTracksRequest([track.Id]),
                        cancellationToken);

                    // Update cache if enabled
                    if (IsCacheEnabled)
                        await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);

                    StatusMessage = $"Removed '{track.Name}' from your liked songs";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to remove track: {ex.Message}";
                }
            }, cancellationToken);
        else
            Task.Run(async () =>
            {
                try
                {
                    if (SelectedPlaylist.Id != null)
                    {
                        await _spotifyClient.Playlists.RemoveItems(SelectedPlaylist.Id, new PlaylistRemoveItemsRequest
                        {
                            Tracks = [new PlaylistRemoveItemsRequest.Item { Uri = track.Uri }]
                        }, cancellationToken);

                        // Update cache if enabled
                        if (IsCacheEnabled && SelectedPlaylist?.Id != null)
                            await SaveTracksToCacheAsync(SelectedPlaylist.Id, Tracks, cancellationToken);
                    }

                    StatusMessage = $"Removed '{track.Name}' from playlist";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to remove track: {ex.Message}";
                }
            }, cancellationToken);
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
                Description = "Songs you've liked on Spotify on Spotify",
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

            // Add an initial batch of playlists
            foreach (var playlist in playlistsResponse.Items!.Where(playlist => playlist.Owner?.Id == user.Id))
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

                            // Batch progress updates - only update the UI when significant changes occur
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

            // Group UI updates at the end
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
            // Operation was canceled, no need to update UI
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

            // Stream-based JSON processing for cache loading
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

                            // Batch progress updates - only update the UI when significant changes occur
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

            // Group UI updates at the end
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Tracks = new ObservableCollection<FullTrack>(allTracks);
                StatusMessage = $"Loaded {allTracks.Count} liked songs";
                IsLoadingTracks = false;
                LoadingProgress = 100;
                this.RaisePropertyChanged(nameof(FilteredTracks));
            });

            // Stream-based JSON processing for cache saving
            if (IsCacheEnabled)
                await SaveLikedTracksToCacheAsync(Tracks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, no need to update UI
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

    // Update the FindDuplicatesAsync method to show the TreeView
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
                _trackItems.ToList()
            );

            var duplicates = duplicatesFinder.FindAllDuplicates();

            DuplicateGroups.Clear();
            foreach (var group in duplicates) DuplicateGroups.Add(group);

            InitializeDuplicatesTreeDataGrid();

            if (DuplicateGroups.Count > 0)
            {
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

    private void RemoveAllDuplicates()
    {
        if (!DuplicateGroups.Any())
        {
            StatusMessage = "No duplicates to remove";
            return;
        }

        var deletedCount = 0;

        foreach (var group in DuplicateGroups)
            // Keep the first track (index 0) and delete the rest
            for (var i = 1; i < group.Tracks.Count; i++)
            {
                DeleteTrack(group.Tracks[i].Track);
                deletedCount++;
            }

        StatusMessage = $"Removed {deletedCount} duplicate tracks";
        DuplicateGroups.Clear();
        IsDuplicateViewVisible = false;

        // Refresh track list
        ForceRefreshTracks();
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
            // ‹Build›: create a Button for each track
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
                // Find which group this track belongs to
                var parentGroup = DuplicateGroups.FirstOrDefault(g => g.Tracks.Contains(track));
                if (parentGroup != null)
                {
                    DeleteTrack(track.Track);

                    // Update the duplicate group
                    parentGroup.Tracks.Remove(track);

                    // If only one track remains, remove the group
                    if (parentGroup.Tracks.Count <= 1)
                        DuplicateGroups.Remove(parentGroup);

                    // Rebuild the TreeDataGrid to reflect changes
                    InitializeDuplicatesTreeDataGrid();

                    StatusMessage = $"Removed track: {track.Name}";
                }

                break;

            case Duplicates.DuplicateGroup group:
                // Delete all tracks except the first one
                for (var i = 1; i < group.Tracks.Count; i++)
                    DeleteTrack(group.Tracks[i].Track);

                DuplicateGroups.Remove(group);

                // Rebuild the TreeDataGrid to reflect changes
                InitializeDuplicatesTreeDataGrid();

                StatusMessage = "Removed duplicate group";
                break;
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
                return Task.CompletedTask;
            });
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