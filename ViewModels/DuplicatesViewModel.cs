using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyPlaylistCleaner_DotNET.Models;
using SpotifyPlaylistCleaner_DotNET.Services;

namespace SpotifyPlaylistCleaner_DotNET.ViewModels;

public class DuplicatesViewModel : ViewModelBase, IDisposable
{
    private readonly IDuplicatesService _duplicatesService;
    private readonly ISpotifyService _spotifyService;
    private CancellationTokenSource? _cancellationTokenSource;

    private ObservableCollection<DuplicateGroup> _duplicateGroups = [];
    private ObservableCollection<DuplicateGroup> _filteredDuplicateGroups = [];
    private string _searchQuery = string.Empty;
    private bool _isDuplicatesViewVisible;
    private PlaylistModel? _currentPlaylist;

    public ObservableCollection<DuplicateGroup> DuplicateGroups
    {
        get => _duplicateGroups;
        private set => this.RaiseAndSetIfChanged(ref _duplicateGroups, value);
    }

    public ObservableCollection<DuplicateGroup> FilteredDuplicateGroups
    {
        get => _filteredDuplicateGroups;
        set => this.RaiseAndSetIfChanged(ref _filteredDuplicateGroups, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            FilterDuplicates();
        }
    }

    public bool IsDuplicatesViewVisible
    {
        get => _isDuplicatesViewVisible;
        set => this.RaiseAndSetIfChanged(ref _isDuplicatesViewVisible, value);
    }

    private PlaylistModel? CurrentPlaylist
    {
        get => _currentPlaylist;
        set => this.RaiseAndSetIfChanged(ref _currentPlaylist, value);
    }


    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler? BackToTracksView;
    public event EventHandler? DuplicatesRemoved;

    public DuplicatesViewModel(
        IDuplicatesService duplicatesService,
        ISpotifyService spotifyService
        )
    {
        _duplicatesService = duplicatesService;
        _spotifyService = spotifyService;

        FindDuplicatesCommand = ReactiveCommand.Create<ObservableCollection<TrackModel>>(FindDuplicates);
        RemoveAllDuplicatesCommand = ReactiveCommand.Create(RemoveAllDuplicates);
        DeleteDuplicateCommand = ReactiveCommand.Create<TrackModel>(DeleteDuplicate);
        DeleteGroupDuplicatesCommand = ReactiveCommand.Create<DuplicateGroup>(DeleteGroupDuplicates);
        BackToTracksCommand = ReactiveCommand.Create(() =>
        {
            IsDuplicatesViewVisible = false;
            BackToTracksView?.Invoke(this, EventArgs.Empty);
        });
        ResetFiltersCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);
    }

    public ICommand FindDuplicatesCommand { get; }
    public ICommand RemoveAllDuplicatesCommand { get; }
    public ICommand DeleteDuplicateCommand { get; }
    public ICommand DeleteGroupDuplicatesCommand { get; }
    public ICommand BackToTracksCommand { get; }
    public ICommand ResetFiltersCommand { get; }

    private void FindDuplicates(ObservableCollection<TrackModel> tracks)
    {
        if (tracks.Count == 0)
        {
            OnStatusMessageChanged("No tracks to analyze for duplicates");
            return;
        }

        try
        {
            if (CurrentPlaylist == null && tracks.Any())
            {
                var mainViewModel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow?.DataContext as MainWindowViewModel
                    : null;

                if (mainViewModel != null)
                {
                    CurrentPlaylist = mainViewModel.TrackListViewModel.CurrentPlaylist;
                }
            }

            OnStatusMessageChanged("Finding duplicates...");
            var duplicates = _duplicatesService.FindDuplicates(tracks);

            DuplicateGroups = new ObservableCollection<DuplicateGroup>(duplicates);
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(duplicates);
            SearchQuery = string.Empty;


            if (DuplicateGroups.Count > 0)
            {
                IsDuplicatesViewVisible = true;
                OnStatusMessageChanged($"Found {DuplicateGroups.Count} duplicate groups");
            }
            else
            {
                OnStatusMessageChanged("No duplicates found in this playlist");
            }
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Error finding duplicates: {ex.Message}");
        }
    }

    private async void RemoveAllDuplicates()
    {
        try
        {
            if (DuplicateGroups.Count == 0 || CurrentPlaylist == null)
            {
                OnStatusMessageChanged("No duplicates to remove");
                return;
            }

            CancelCurrentOperations();
            var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

            try
            {
                var tracksToDelete = CollectTracksToDelete();
                
                var trackUris = tracksToDelete.Select(t => t.Uri).ToList();

                if (tracksToDelete.Count == 0)
                {
                    OnStatusMessageChanged("No tracks to delete after applying rules");
                    return;
                }

                var successCount = 0;
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (CurrentPlaylist.IsLikedSongs)
                    {
                        await _spotifyService.RemoveTracksFromLikedSongs(trackUris, cancellationToken);                    }
                    else
                    {
                        await _spotifyService.RemoveTracksFromPlaylist(CurrentPlaylist.Id, trackUris,cancellationToken);
                    }

                    successCount = tracksToDelete.Count;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error removing tracks: {ex.Message}");
                }

                DuplicateGroups.Clear();
                IsDuplicatesViewVisible = false;
                OnStatusMessageChanged($"Successfully removed {successCount} of {tracksToDelete.Count} duplicate tracks using smart selection");

                DuplicatesRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnStatusMessageChanged($"Error removing duplicates: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            OnStatusMessageChanged($"Error processing duplicates: {e.Message}");
        }
    }

    private void DeleteDuplicate(TrackModel track)
    {
        if (CurrentPlaylist == null)
            return;

        CancelCurrentOperations();
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            var group = DuplicateGroups.FirstOrDefault(g => g.Tracks.Contains(track));
            if (group == null)
                return;

            // Remove track from the group
            group.Tracks.Remove(track);
            
            // Update both collections to ensure UI refresh
            var updatedGroups = new ObservableCollection<DuplicateGroup>([.. DuplicateGroups]);
            DuplicateGroups = updatedGroups;
            
            // Update filtered collection as well
            FilterDuplicates();
            
            // Remove group if no tracks left
            if (group.Tracks.Count <= 1)
            {
                DuplicateGroups.Remove(group);
                FilterDuplicates(); // Apply filtering again
            }

            Task.Run(async () =>
            {
                try
                {
                    if (CurrentPlaylist.IsLikedSongs)
                    {
                        await _spotifyService.RemoveTrackFromLikedSongs(track.Id, cancellationToken);
                    }
                    else
                    {
                        await _spotifyService.RemoveTrackFromPlaylist(CurrentPlaylist.Id, track.Uri, cancellationToken);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Removed track: {track.Name}");

                        // Get reference to the TrackListViewModel to refresh the tracks
                        var mainViewModel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow?.DataContext as MainWindowViewModel
                            : null;

                        mainViewModel?.TrackListViewModel.RefreshTracks();

                        if (DuplicateGroups.Count != 0) return;
                        IsDuplicatesViewVisible = false;
                        BackToTracksView?.Invoke(this, EventArgs.Empty);
                        DuplicatesRemoved?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Error removing track: {ex.Message}");
                    });
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Error processing duplicate: {ex.Message}");
        }
    }

    private void DeleteGroupDuplicates(DuplicateGroup group)
    {
        if (CurrentPlaylist == null || group.Tracks.Count <= 1)
            return;

        CancelCurrentOperations();
        var cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        try
        {
            // Determine which track to keep based on the same rules as RemoveAllDuplicates
            var indexToKeep = _duplicatesService.GetTrackToKeepIndex(group);
            var tracksToDelete = group.Tracks.Where((_, i) => i != indexToKeep).ToList();
            var trackToKeep = group.Tracks[indexToKeep];

            // No need to proceed if there are no tracks to delete
            if (tracksToDelete.Count == 0)
            {
                OnStatusMessageChanged("No tracks to delete after applying rules");
                return;
            }

            // Create a copy of tracks to delete before modifying the group
            var tracksToDeleteCopy = tracksToDelete.ToList();

            // Update UI immediately by removing tracks from the group
            foreach (var track in tracksToDelete)
            {
                group.Tracks.Remove(track);
            }

            // Create a brand-new collection to force change notification
            DuplicateGroups = new ObservableCollection<DuplicateGroup>([.. DuplicateGroups]);
            FilterDuplicates();

            // If only one track remains (the one we're keeping), remove the group
            if (group.Tracks.Count <= 1)
            {
                DuplicateGroups.Remove(group);
                FilterDuplicates();
            }

            Task.Run(async () =>
            {
                var successCount = 0;
                
                try
                {
                    foreach (var track in tracksToDeleteCopy)
                    {
                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            if (CurrentPlaylist.IsLikedSongs)
                            {
                                await _spotifyService.RemoveTrackFromLikedSongs(track.Id, cancellationToken);
                            }
                            else
                            {
                                await _spotifyService.RemoveTrackFromPlaylist(CurrentPlaylist.Id, track.Uri, cancellationToken);
                            }
                            
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error removing track {track.Name}: {ex.Message}");
                        }
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Removed {successCount} of {tracksToDeleteCopy.Count} duplicates from group \"{group.DisplayName}\", kept \"{trackToKeep.Name}\"");

                        // Get reference to the TrackListViewModel to refresh the tracks
                        var mainViewModel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                            ? desktop.MainWindow?.DataContext as MainWindowViewModel
                            : null;

                        mainViewModel?.TrackListViewModel.RefreshTracks();

                        if (DuplicateGroups.Count != 0) return;
                        IsDuplicatesViewVisible = false;
                        BackToTracksView?.Invoke(this, EventArgs.Empty);
                        DuplicatesRemoved?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        OnStatusMessageChanged($"Error removing tracks from group: {ex.Message}");
                    });
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            OnStatusMessageChanged($"Error processing group duplicates: {ex.Message}");
        }
    }

    private List<TrackModel> CollectTracksToDelete()
    {
        var tracksToDelete = new List<TrackModel>();

        foreach (var group in DuplicateGroups)
        {
            if (group.Tracks.Count <= 1)
                continue;

            var indexToKeep = _duplicatesService.GetTrackToKeepIndex(group);

            tracksToDelete.AddRange(group.Tracks.Where((_, i) => i != indexToKeep));
        }

        return tracksToDelete;
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

    private void FilterDuplicates()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(DuplicateGroups);
        }
        else
        {
            var lowerCaseQuery = SearchQuery.ToLowerInvariant();
            FilteredDuplicateGroups = new ObservableCollection<DuplicateGroup>(
                DuplicateGroups.Where(group =>
                    group.DisplayName.Contains(lowerCaseQuery, StringComparison.InvariantCultureIgnoreCase) ||
                    group.DisplayArtist.Contains(lowerCaseQuery, StringComparison.InvariantCultureIgnoreCase) ||
                    group.Tracks.Any(track =>
                        track.Name.Contains(lowerCaseQuery, StringComparison.InvariantCultureIgnoreCase) ||
                        track.ArtistNames.Contains(lowerCaseQuery, StringComparison.InvariantCultureIgnoreCase)
                    )
                )
            );
        }
    }
}
